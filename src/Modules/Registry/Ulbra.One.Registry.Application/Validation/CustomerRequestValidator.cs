using FluentValidation;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Registry.Application.Validation;

public sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(x => x.CompanyId).Must(x => x.Value != Guid.Empty).WithMessage("CompanyId is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0).When(x => x.CreditLimit.HasValue);
        RuleFor(x => x.Document)
            .Must((req, doc) => req.PersonType == PersonType.Company
                ? DocumentValidator.IsValidCnpj(doc)
                : DocumentValidator.IsValidCpf(doc))
            .WithMessage("Invalid document (CPF/CNPJ).").WithErrorCode(ErrorCodes.Document.Invalid);
        RuleFor(x => x.Address!).SetValidator(new AddressRequestValidator()).When(x => x.Address is not null);
    }
}
