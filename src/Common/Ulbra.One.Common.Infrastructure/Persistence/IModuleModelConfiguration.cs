using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ulbra.One.Common.Infrastructure.Persistence;

/// <summary>
/// A module's contribution to the shared AppDbContext model. Modules register their own
/// implementation in DI; the context iterates them at model-creation time and never
/// references module projects.
/// </summary>
public interface IModuleModelConfiguration
{
    void Apply(ModelBuilder modelBuilder);
}

/// <summary>Default contribution: applies every IEntityTypeConfiguration found in the module's assembly.</summary>
public sealed class AssemblyModelConfiguration(Assembly assembly) : IModuleModelConfiguration
{
    /// <summary>The scanned assembly. Exposed so tests can identify which module a contribution belongs to.</summary>
    public Assembly Assembly { get; } = assembly;

    public void Apply(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(Assembly);
}

public static class ModelConfigurationRegistration
{
    /// <summary>Registers the assembly scan. Pass the module's own type, e.g. typeof(FinanceModule).Assembly.</summary>
    public static IServiceCollection AddModelConfiguration(this IServiceCollection services, Assembly assembly) =>
        services.AddSingleton<IModuleModelConfiguration>(new AssemblyModelConfiguration(assembly));

    /// <summary>Registers a custom contribution, for mapping that isn't a plain assembly scan.</summary>
    public static IServiceCollection AddModelConfiguration<T>(this IServiceCollection services)
        where T : class, IModuleModelConfiguration =>
        services.AddSingleton<IModuleModelConfiguration, T>();
}
