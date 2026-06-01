using Microsoft.EntityFrameworkCore;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database;

public class CoreContext(DbContextOptions<CoreContext> options) : DbContext(options)
{
    public const string Schema = "core";

    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Unidade> Unidades => Set<Unidade>();
    public DbSet<CentroDeCusto> CentrosDeCusto => Set<CentroDeCusto>();
    public DbSet<CentroDeCustoUnidade> CentroDeCustoUnidades => Set<CentroDeCustoUnidade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Empresa>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RazaoSocial).HasMaxLength(200).IsRequired();
            e.Property(x => x.Cnpj).HasMaxLength(14).IsRequired();
            e.HasIndex(x => x.Cnpj).IsUnique();
        });

        modelBuilder.Entity<Unidade>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(200).IsRequired();
            e.Property(x => x.Sigla).HasMaxLength(20).IsRequired();
            e.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EmpresaId, x.Sigla }).IsUnique();
        });

        modelBuilder.Entity<CentroDeCusto>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Codigo).HasMaxLength(50).IsRequired();
            e.Property(x => x.Nome).HasMaxLength(200).IsRequired();
            e.Property(x => x.OrcamentoAnual).HasPrecision(18, 2);
            e.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EmpresaId, x.Codigo }).IsUnique();
        });

        modelBuilder.Entity<CentroDeCustoUnidade>(e =>
        {
            e.HasKey(x => new { x.CentroDeCustoId, x.UnidadeId });
            e.HasOne<CentroDeCusto>().WithMany().HasForeignKey(x => x.CentroDeCustoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Unidade>().WithMany().HasForeignKey(x => x.UnidadeId).OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}
