namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>Which chart-of-accounts template to seed for a company.</summary>
public enum ChartTemplate
{
    /// <summary>Minimal generic template (dev/demo).</summary>
    Demo,
    /// <summary>RFB Plano Referencial — entidade imune/isenta (sem fins lucrativos).</summary>
    RfbImunesIsentas,
    /// <summary>RFB Plano Referencial — PJ Lucro Real (com fins lucrativos).</summary>
    RfbPjLucroReal
}
