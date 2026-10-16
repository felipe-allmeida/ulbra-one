namespace Ulbra.One.Registry.Domain.Entities;

/// <summary>Individual (CPF) or legal entity (CNPJ).</summary>
public enum PersonType
{
    Individual = 0, // CPF
    Company = 1,    // CNPJ
}
