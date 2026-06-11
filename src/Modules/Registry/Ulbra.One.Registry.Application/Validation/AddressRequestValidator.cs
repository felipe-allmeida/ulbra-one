using FluentValidation;

namespace Ulbra.One.Registry.Application.Validation;

/// <summary>Validates an optional address. CEP, when present, must be exactly 8 digits (mask is ignored).</summary>
public sealed class AddressRequestValidator : AbstractValidator<AddressRequest>
{
    public AddressRequestValidator()
    {
        RuleFor(x => x.Cep)
            .Must(cep => AddressMapping.NormalizeCep(cep).Length == 8)
            .When(x => !string.IsNullOrWhiteSpace(x.Cep))
            .WithMessage("Invalid CEP (must have 8 digits).").WithErrorCode("Cep.Invalid");
    }
}
