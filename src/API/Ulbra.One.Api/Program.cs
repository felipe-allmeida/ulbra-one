using System.Globalization;
using System.Threading.RateLimiting;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.SwaggerGen;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Domain.Errors;
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
using Ulbra.One.Budgeting.Application;
using Ulbra.One.Budgeting.Infrastructure;
using Ulbra.One.Notifications.Application;
using Ulbra.One.Notifications.Infrastructure;
using Ulbra.One.Fiscal.Application;
using Ulbra.One.Fiscal.Infrastructure;
using Ulbra.One.Common.Infrastructure.Ai;
using Ulbra.One.Common.Infrastructure.ErrorHandling;
using Ulbra.One.Common.Infrastructure.Observability;
using Ulbra.One.Common.Application.Realtime.Endpoints;
using Ulbra.One.Common.Infrastructure.Realtime;
using Ulbra.One.Common.Infrastructure.Security;
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

builder.Services.AddAppDataProtection(builder.Configuration);

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
    // Without this the browser reissues the preflight before every request: 47% of production
    // traffic was OPTIONS. One hour is inside Chrome's 7200s cap.
    var preflightMaxAge = TimeSpan.FromHours(1);

    options.AddDefaultPolicy(policy =>
    {
        // Expose Content-Disposition so the SPA (cross-origin) can read the server's canonical download
        // file name (remittance/attachment/export downloads); it is not a CORS-safelisted header.
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials()
                .WithExposedHeaders("Content-Disposition")
                .SetPreflightMaxAge(preflightMaxAge);
        }
        else
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
                .WithExposedHeaders("Content-Disposition")
                .SetPreflightMaxAge(preflightMaxAge);
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
        await ErrorResults.TooManyRequests(
            "Too many requests. Please try again later.",
            ErrorCodes.RateLimit.Exceeded,
            context.HttpContext.Request.Path).ExecuteAsync(context.HttpContext);
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

    // Endpoint documentation (docs/api-design.md): request DTOs and [AsParameters] query objects describe
    // each field with [SwaggerSchema] and sample it with [SwaggerExample]. Annotations must be enabled for
    // Swashbuckle to read the former at all — without this the attributes are a silent no-op.
    options.EnableAnnotations();
    options.SchemaFilter<Ulbra.One.Api.ExampleSchemaFilter>();
    options.SchemaFilter<Ulbra.One.Api.RefPropertyDescriptionSchemaFilter>();
    options.SchemaFilter<Ulbra.One.Api.ProblemDetailsSchemaFilter>();
    options.ParameterFilter<Ulbra.One.Api.ParameterDescriptionFilter>();
    options.ParameterFilter<Ulbra.One.Api.PrefixedIdExampleParameterFilter>();
    options.ParameterFilter<Ulbra.One.Api.CamelCaseQueryParameterFilter>();

    // Vertical-slice endpoints declare nested DTOs named `Request`/`Response` (often just those bare names,
    // reused across slices — e.g. ClassifyPayableBatch+Response and ClearReconciliation+Response). Without
    // disambiguation Swashbuckle would assign the same schemaId to all of them and fail. Prefix every nested
    // `*Request`/`*Response` type with its declaring type name (e.g. CreateCostCenterRequest, DevTokenRequest,
    // ClearReconciliationResponse); generics get their argument names as a prefix (e.g. VendorResponsePaginatedResponse).
    static string SchemaId(Type type)
    {
        if (type.IsGenericType)
        {
            var args = string.Concat(type.GetGenericArguments().Select(SchemaId));
            var name = type.Name[..type.Name.IndexOf('`')];
            return args + name;
        }
        // Nested endpoint DTOs (…Request/…Response) share names across endpoints — "Request"/"Response"
        // (ClearReconciliation.Response, ClassifyPayableBatch.Response) or "RejectRequest" (RejectPayable vs
        // RejectTransaction) — which collide in the OpenAPI schema repository (→ swagger 500). Qualify them with
        // the declaring endpoint's name for a unique schemaId, unless the name already starts with it (e.g.
        // SetReconciledBalance.SetReconciledBalanceResponse) — qualifying that would double it.
        if (type is { IsNested: true, DeclaringType: not null }
            && (type.Name.EndsWith("Request", StringComparison.Ordinal) || type.Name.EndsWith("Response", StringComparison.Ordinal)))
            return type.Name.StartsWith(type.DeclaringType.Name, StringComparison.Ordinal)
                ? type.Name
                : type.DeclaringType.Name + type.Name;
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
builder.Services.AddCoreValidators();
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddAccountingModule(builder.Configuration);
builder.Services.AddFiscalModule(builder.Configuration);
builder.Services.AddBudgetingModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddRealtime(builder.Configuration, builder.Environment);

// Assembled from every module that registered an IChargeReferenceSource above — the same linha digitável must not
// be queued for payment from a Finance title and a requisition plan at once.
builder.Services.AddScoped<ChargeReferenceDuplicateChecker>();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddObjectStorage(builder.Configuration);
builder.Services.AddAi(builder.Configuration);
// Short-lived cache so a fiscal-document preview (AI vision extraction) can be reused by the subsequent
// save, avoiding a second paid extraction of the same blob. Best-effort (per-instance).
builder.Services.AddMemoryCache();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(connectionString, name: "database", tags: ["ready"]);
// O check do backplane de realtime é registrado em AddRealtime, com a tag "realtime" — nunca "ready".
// Ver RealtimeBackplaneHealthCheck: degrada, não derruba.

var app = builder.Build();

// Run-once job mode for Azure Container Apps scheduled Jobs (e.g. `--job remittance-sweep`):
// resolve the service, run to completion, and exit with a status code — skipping the web pipeline,
// migrations and dev seeding. Hosted services never start (app.Run() is not called), so a scheduled
// run and the web replicas do not double-execute the sweep.
if (args is ["--job", var jobName, ..])
{
    var jobLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var jobClock = app.Services.GetRequiredService<TimeProvider>();
    return await Ulbra.One.Api.JobRunner.RunAsync(app.Services, jobName, jobClock, jobLogger, CancellationToken.None);
}

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
    // Mock business data (companies/vendors/payables) stays off; seed only the reference data the
    // payable pipeline needs: the global AELBRA chart of accounts (LedgerAccount) for the AI classifier,
    // and the group-wide withholding rules so the auto-retention calculation produces output.
    await Ulbra.One.Api.DevDataSeeder.SeedLedgerChartAsync(app.Services);
    await Ulbra.One.Api.DevDataSeeder.SeedWithholdingRulesAsync(app.Services);
    await Ulbra.One.Api.DevDataSeeder.SeedAsync(app.Services);
    // Local test users (one per role) to exercise claim-based authorization.
    await Ulbra.One.Api.DevDataSeeder.SeedLocalUsersAsync(app.Services);
    // Assign the seeded requisitions to the dev requester (runs after the seed, which stamps "system").
    await Ulbra.One.Api.DevDataSeeder.AssignSeedRequisitionsToRequesterAsync(app.Services);
    // Grants role users (non-Admin) company scope, otherwise they are fail-closed for blobs (AEL-764).
    await Ulbra.One.Api.DevDataSeeder.SeedLocalUserScopeGrantsAsync(app.Services);
    // Seeds the unified scope (units + cost centers) for scoped-role dev users (Requester/Approver),
    // otherwise they are fail-closed under AEL-883 (empty scope = create/approve/see nothing).
    await Ulbra.One.Api.DevDataSeeder.SeedLocalScopedRoleGrantsAsync(app.Services);
    // Additive showcase seed: fills the states the seed above cannot reach on an already-populated database
    // (its Companies.Count == 0 gate makes it a no-op there) — payables in every status, a reconciliation
    // covering every match verdict, CNAB remittances + returns, receivables, accounting and budgeting.
    // Idempotent per section and non-destructive; never throws (logs and continues).
    await Ulbra.One.Api.DevShowcaseSeeder.SeedAsync(app.Services);
    app.UseSwagger();
    app.UseSwaggerUI();
}
else if (app.Environment.IsProduction())
{
    // Foundation seed (AELBRA org, ledger chart, withholding rules, expense categories — NO budget,
    // NO admins/users, NO mock data). Serialize across replicas with a Postgres session advisory lock, and
    // log-and-continue on failure so a seed error doesn't crash-loop the app (every step is idempotent).
    using var seedScope = app.Services.CreateScope();
    var seedLogger = seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    const long seedLockKey = 427530; // distinct from migrationLockKey; serializes the foundation seed across replicas
    try
    {
        await using var lockConn = new Npgsql.NpgsqlConnection(connectionString);
        await lockConn.OpenAsync();
        try
        {
            await using (var lockCmd = lockConn.CreateCommand())
            {
                lockCmd.CommandText = "SELECT pg_advisory_lock(@key)";
                lockCmd.Parameters.AddWithValue("key", seedLockKey);
                await lockCmd.ExecuteNonQueryAsync();
            }
            await Ulbra.One.Api.ProdDataSeeder.SeedAsync(app.Services);
        }
        finally
        {
            await using var unlockCmd = lockConn.CreateCommand();
            unlockCmd.CommandText = "SELECT pg_advisory_unlock(@key)";
            unlockCmd.Parameters.AddWithValue("key", seedLockKey);
            await unlockCmd.ExecuteNonQueryAsync();
        }
    }
    catch (Exception ex)
    {
        seedLogger.LogError(ex, "Production foundation seed failed; the app will start but the foundation may be incomplete — investigate.");
    }
}

// Security migration (all environments): grants the single company to users with no grant, so
// fail-closed blob access (AEL-764) doesn't break existing users. No-op in multi-company. Idempotent.
await Ulbra.One.Api.DevDataSeeder.BackfillUserScopeGrantsAsync(app.Services);

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
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).AllowAnonymous();
// Backward-compatible default = readiness, mais os checks acessórios (realtime): eles só sabem
// reportar Degraded, que o middleware mapeia para 200, então aparecem no corpo sem mudar o status HTTP
// nem o que /health/ready decide sobre o tráfego.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready") || r.Tags.Contains(RealtimeSetup.HealthCheckTag),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).AllowAnonymous();

var apiGroup = app.MapGroup("/api");
apiGroup.MapIdentityEndpoints();
apiGroup.MapCoreEndpoints();
apiGroup.MapFinanceEndpoints();
apiGroup.MapRegistryEndpoints();
apiGroup.MapAuditEndpoints();
apiGroup.MapAccountingEndpoints();
apiGroup.MapFiscalEndpoints();
apiGroup.MapBudgetingEndpoints();
apiGroup.MapNotificationsEndpoints();
apiGroup.MapRealtimeEndpoints();

app.Run();

return 0;

public partial class Program;
