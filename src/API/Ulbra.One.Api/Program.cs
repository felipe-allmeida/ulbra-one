using System.Globalization;
using System.Threading.RateLimiting;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Core.Application;
using Ulbra.One.Core.Infrastructure;
using Ulbra.One.Identity.Application;
using Ulbra.One.Identity.Infrastructure;
using Ulbra.One.Finance.Application;
using Ulbra.One.Finance.Infrastructure;
using Ulbra.One.Registry.Application;
using Ulbra.One.Registry.Infrastructure;
using Ulbra.One.Audit.Application;
using Ulbra.One.Audit.Infrastructure;
using Ulbra.One.Accounting.Application;
using Ulbra.One.Accounting.Infrastructure;
using Ulbra.One.Common.Infrastructure.ErrorHandling;
using Ulbra.One.Common.Infrastructure.Observability;
using Ulbra.One.Common.Infrastructure.Storage;
using Ulbra.One.Api;
using Ulbra.One.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Azure Key Vault — opt-in config source. Set KeyVault:Uri in env/appsettings to enable.
// Secrets from the vault (e.g. ConnectionStrings--Database, Authentication--JwtSecret) override
// appsettings/env values. Inert when unset → local/dev behaviour unchanged.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new Azure.Identity.DefaultAzureCredential());

// Observability: OpenTelemetry tracing/metrics + structured JSON logging (config-driven, no-op without a collector).
builder.AddAppObservability();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddAppProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();

// CORS
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
    });
});

// Rate limiting (built-in Microsoft.AspNetCore.RateLimiting) — defense-in-depth.
// Limits are configurable (section "RateLimiting") and effectively disabled in Development so the
// integration tests + Playwright e2e (which both run under the Development environment and hammer
// /auth/dev-token) never trip a 429. The protection bites in Staging/Production.
var rateLimitingEnabled = !builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue("RateLimiting:Enabled", true);

// Window is shared by the auth and global limiters.
var rlWindow = TimeSpan.FromMinutes(1);
var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 15);
var globalPermitLimit = builder.Configuration.GetValue("RateLimiting:GlobalPermitLimit", 200);

static string ClientKey(HttpContext ctx) =>
    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Named "auth" policy: tight IP-keyed fixed window to slow brute-force on credential endpoints.
    options.AddPolicy("auth", ctx => rateLimitingEnabled
        ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPermitLimit,
            Window = rlWindow,
        })
        : RateLimitPartition.GetNoLimiter("disabled"));

    // Generous global fallback so normal app usage never trips.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => rateLimitingEnabled
        ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = globalPermitLimit,
            Window = rlWindow,
        })
        : RateLimitPartition.GetNoLimiter("disabled"));

    options.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await TypedResults.Problem(
            title: "RateLimit.Exceeded",
            detail: "Too many requests. Please try again later.",
            statusCode: StatusCodes.Status429TooManyRequests,
            instance: context.HttpContext.Request.Path).ExecuteAsync(context.HttpContext);
    };
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "ULBRA One API",
        Version = "v1"
    });

    var jwtScheme = new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Description = "JWT obtido em /api/auth/dev-token ou /api/auth/google"
    };
    options.AddSecurityDefinition("Bearer", jwtScheme);

    // Vertical-slice endpoints declare nested DTOs whose names end in `Request` (often just `Request`,
    // but also reused names like `RejectRequest` across slices). Without disambiguation Swashbuckle would
    // assign the same schemaId to all of them and fail. Prefix every nested `*Request` type with its
    // declaring type name (e.g. CreateCostCenterRequest, DevTokenRequest, RejectPayableRejectRequest);
    // generics get their argument names as a prefix (e.g. VendorResponsePaginatedResponse).
    static string SchemaId(Type type)
    {
        if (type.IsGenericType)
        {
            var args = string.Concat(type.GetGenericArguments().Select(SchemaId));
            var name = type.Name[..type.Name.IndexOf('`')];
            return args + name;
        }
        if (type is { IsNested: true, DeclaringType: not null } && type.Name.EndsWith("Request", StringComparison.Ordinal))
            return type.DeclaringType.Name + type.Name;
        return type.Name;
    }
    options.CustomSchemaIds(SchemaId);

    // Global JsonStringEnumConverter serializes enums as strings; describe them as string enums in the spec.
    options.SchemaFilter<Ulbra.One.Api.StringEnumSchemaFilter>();

    // C# non-nullable reference types map to non-nullable schema; nullable members keep `nullable: true`.
    options.SupportNonNullableReferenceTypes();

    // DTO members are always present on the wire (records); mark every property `required` so generated
    // TS fields are non-optional. Genuinely nullable members remain `T | null`.
    options.SchemaFilter<Ulbra.One.Api.RequiredPropertiesSchemaFilter>();

    // Prefixed typed ids serialize as opaque strings (prefix_<guid:N>); describe them as string in the spec
    // so generated TS keeps them as `string`. The registry is source-generated from all [PrefixedId] structs.
    static Microsoft.OpenApi.OpenApiSchema IdSchema(string example) => new()
    {
        Type = Microsoft.OpenApi.JsonSchemaType.String,
        Example = System.Text.Json.Nodes.JsonValue.Create(example),
    };
    foreach (var (type, example) in Ulbra.One.Common.Application.Ids.PrefixedIdRegistry.All)
        options.MapType(type, () => IdSchema(example));
});

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

// Modules
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddCoreModule(builder.Configuration);
builder.Services.AddFinanceModule(builder.Configuration);
builder.Services.AddRegistryModule(builder.Configuration);
builder.Services.AddRegistryValidators();
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddAccountingModule(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddObjectStorage(builder.Configuration);

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(connectionString, name: "database", tags: ["ready"]);

var app = builder.Build();

// Apply migrations on startup (Postgres may not be ready when the API starts up).
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var attempts = 0;
    const int maxAttempts = 30;
    var delay = TimeSpan.FromSeconds(2);
    const long migrationLockKey = 427529; // arbitrary fixed key; serializes migrations across replicas
    while (true)
    {
        try
        {
            // Hold a Postgres session-level advisory lock on a dedicated connection so concurrent
            // app instances apply migrations one at a time instead of racing the same DDL.
            await using var lockConn = new Npgsql.NpgsqlConnection(connectionString);
            await lockConn.OpenAsync();
            await using (var lockCmd = lockConn.CreateCommand())
            {
                lockCmd.CommandText = "SELECT pg_advisory_lock(@key)";
                lockCmd.Parameters.AddWithValue("key", migrationLockKey);
                await lockCmd.ExecuteNonQueryAsync();
            }
            try
            {
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<AuditContext>().Database.MigrateAsync();
            }
            finally
            {
                await using var unlockCmd = lockConn.CreateCommand();
                unlockCmd.CommandText = "SELECT pg_advisory_unlock(@key)";
                unlockCmd.Parameters.AddWithValue("key", migrationLockKey);
                await unlockCmd.ExecuteNonQueryAsync();
            }
            break;
        }
        catch (Exception ex) when (attempts++ < maxAttempts)
        {
            logger.LogWarning(ex, "Migration attempt {Attempt}/{Max} failed; retrying in {Delay}s", attempts, maxAttempts, delay.TotalSeconds);
            await Task.Delay(delay);
        }
    }
}

if (app.Environment.IsDevelopment())
{
    await Ulbra.One.Api.DevDataSeeder.SeedAsync(app.Services);
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.UseAppExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

// Liveness: process is up (no external deps). Readiness: dependencies (DB) are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
// Backward-compatible default = readiness.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

var apiGroup = app.MapGroup("/api");
apiGroup.MapIdentityEndpoints();
apiGroup.MapCoreEndpoints();
apiGroup.MapFinanceEndpoints();
apiGroup.MapRegistryEndpoints();
apiGroup.MapAuditEndpoints();
apiGroup.MapAccountingEndpoints();

app.Run();

public partial class Program;
