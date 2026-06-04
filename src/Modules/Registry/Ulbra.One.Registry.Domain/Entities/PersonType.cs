namespace Ulbra.One.Registry.Domain.Entities;

/// <summary>Pessoa Física (CPF) ou Pessoa Jurídica (CNPJ).</summary>
public enum PersonType
{
    Individual = 0, // PF
    Company = 1,    // PJ
}
