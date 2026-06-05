using FluentValidation;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Registry.Application.Validation;

public sealed class VendorRequestValidator : AbstractValidator<VendorRequest>
{
    public VendorRequestValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Document)
            .Must((req, doc) => req.PersonType == PersonType.Company
                ? DocumentValidator.IsValidCnpj(doc)
                : DocumentValidator.IsValidCpf(doc))
            .WithMessage("Invalid document (CPF/CNPJ).").WithErrorCode("Document.Invalid");
    }
}
