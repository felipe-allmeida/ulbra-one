using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Core.Application;
using Ulbra.One.Core.Infrastructure;
using Ulbra.One.Core.Infrastructure.Database;

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
});

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Connection string 'Database' não configurada.");

// Modules
builder.Services.AddCoreModule(builder.Configuration);

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
            await scope.ServiceProvider.GetRequiredService<CoreContext>().Database.MigrateAsync();
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

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapGroup("/api").MapCoreEndpoints();

app.Run();

public partial class Program;
