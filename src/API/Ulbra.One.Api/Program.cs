using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Core.Application;
using Ulbra.One.Core.Infrastructure;
using Ulbra.One.Core.Infrastructure.Database;
using Ulbra.One.Identity.Application;
using Ulbra.One.Identity.Infrastructure;
using Ulbra.One.Identity.Infrastructure.Database;
using Ulbra.One.Financeiro.Application;
using Ulbra.One.Financeiro.Infrastructure;
using Ulbra.One.Financeiro.Infrastructure.Database;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddProblemDetails();

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
});

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Connection string 'Database' não configurada.");

// Modules
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddCoreModule(builder.Configuration);
builder.Services.AddFinanceiroModule(builder.Configuration);

// Health checks
builder.Services.AddHealthChecks().AddNpgSql(connectionString);

var app = builder.Build();

// Apply migrations on startup (Postgres pode não estar pronto quando a API sobe).
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var attempts = 0;
    const int maxAttempts = 30;
    var delay = TimeSpan.FromSeconds(2);
    while (true)
    {
        try
        {
            await scope.ServiceProvider.GetRequiredService<IdentityContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<CoreContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<FinanceiroContext>().Database.MigrateAsync();
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
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

var apiGroup = app.MapGroup("/api");
apiGroup.MapIdentityEndpoints();
apiGroup.MapCoreEndpoints();
apiGroup.MapFinanceiroEndpoints();

app.Run();

public partial class Program;
