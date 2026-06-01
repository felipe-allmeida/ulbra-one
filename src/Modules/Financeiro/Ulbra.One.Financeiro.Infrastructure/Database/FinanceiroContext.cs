using Microsoft.EntityFrameworkCore;
using Ulbra.One.Financeiro.Domain.Entities;

namespace Ulbra.One.Financeiro.Infrastructure.Database;

public class FinanceiroContext(DbContextOptions<FinanceiroContext> options) : DbContext(options)
{
    public const string Schema = "financeiro";

    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Lancamento>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Descricao).HasMaxLength(300).IsRequired();
            e.Property(x => x.Contraparte).HasMaxLength(200);
            e.Property(x => x.Valor).HasPrecision(18, 2);
            e.Property(x => x.MotivoRejeicao).HasMaxLength(500);
            // EmpresaId/UnidadeId/CentroDeCustoId: referências lógicas ao módulo Core (sem FK cross-context)
            e.HasIndex(x => x.EmpresaId);
            e.HasIndex(x => x.CentroDeCustoId);
            e.HasIndex(x => new { x.Tipo, x.Status });
        });

        base.OnModelCreating(modelBuilder);
    }
}
