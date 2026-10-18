using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Ulbra.One.Persistence;

namespace Ulbra.One.Api.Database;

/// <summary>Design-time factory used by the EF Core tools (<c>dotnet ef migrations add</c> / <c>database
/// update</c>). It builds the context directly instead of spinning up the web host, mirroring the runtime
/// options from <c>PersistenceModule</c> (snake_case convention + migrations assembly) and feeding the same
/// module model configurations the DI container would. This keeps the tools off the full application graph —
/// which, on Windows with Smart App Control / WDAC, may block loading freshly-built module assemblies
/// (0x800711C7). Never used at runtime; the connection string is a placeholder because <c>migrations add</c>
/// only inspects the model and never opens a connection. Lives in the API project because that is where the
/// references to every module already are — Ulbra.One.Persistence references none of them.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=ulbra_one_design;Username=postgres;Password=postgres",
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
            // AppDbContext.NameRewriter mirrors this choice to recompute FK constraint names —
            // changing the convention (or the culture) here requires changing it there too.
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options, ModuleModelConfigurations.Create());
    }
}
