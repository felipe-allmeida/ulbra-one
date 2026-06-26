var builder = DistributedApplication.CreateBuilder(args);

// ── Infrastructure ──────────────────────────────────────────────────────────

// Postgres: Aspire injects ConnectionStrings__Database (matching the db resource name),
// which is exactly the key the API reads via GetConnectionString("Database").
var postgres = builder.AddPostgres("postgres");
var db = postgres.AddDatabase("Database");

// RabbitMQ: Aspire manages credentials as ParameterResource.
// The API reads individual MassTransit:Host/Port/User/Password keys (see MessagingSetup.cs),
// so we map them via WithEnvironment from the rabbit resource.
var rabbit = builder.AddRabbitMQ("rabbitmq");

// Azure Storage emulator (Azurite) — blobs container named "attachments".
var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var blobs = storage.AddBlobs("attachments");

// ── API ─────────────────────────────────────────────────────────────────────

var api = builder.AddProject<Projects.Ulbra_One_Api>("api")
    // Postgres: injects ConnectionStrings__Database automatically via WithReference.
    .WithReference(db)
    // RabbitMQ connection info → MassTransit config keys the app reads.
    .WithEnvironment("MassTransit__Transport", "RabbitMq")
    .WithEnvironment(ctx =>
    {
        var ep = rabbit.Resource.PrimaryEndpoint;
        ctx.EnvironmentVariables["MassTransit__Host"] = ep.Host;
        ctx.EnvironmentVariables["MassTransit__Port"] = ep.Port.ToString();
    })
    // Pass ParameterResource directly (not .Value — obsolete in Aspire 9.5)
    .WithEnvironment("MassTransit__User", rabbit.Resource.UserNameParameter!)
    .WithEnvironment("MassTransit__Password", rabbit.Resource.PasswordParameter!)
    // Azure Blob Storage via Azurite.
    .WithEnvironment("ObjectStorage__Provider", "AzureBlob")
    .WithEnvironment("ObjectStorage__Bucket", "attachments")
    // WithReference(blobs) injects ConnectionStrings__attachments, but AddObjectStorage reads
    // ObjectStorage:ConnectionString — bridge the Azurite connection string to that key.
    .WithEnvironment("ObjectStorage__ConnectionString", blobs.Resource.ConnectionStringExpression)
    .WithReference(blobs);

// ── Web (Vite dev server) ───────────────────────────────────────────────────

builder.AddNpmApp("web", "../../web", "dev")
    .WithReference(api)
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment(ctx =>
    {
        // Resolve the API HTTP endpoint so Vite's proxy / VITE_API_BASE_URL is auto-wired.
        if (api.Resource.TryGetLastAnnotation<EndpointAnnotation>(out var ep))
            ctx.EnvironmentVariables["VITE_API_BASE_URL"] = $"http://localhost:{ep.Port}";
    });

builder.Build().Run();
