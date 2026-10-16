using FluentValidation;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Registry.Application.Validation;

public sealed class EmployeeRequestValidator : AbstractValidator<EmployeeRequest>
{
    public EmployeeRequestValidator()
    {
        RuleFor(x => x.CompanyId).Must(x => x.Value != Guid.Empty).WithMessage("CompanyId is required.");
        RuleFor(x => x.UnitId).Must(x => x.Value != Guid.Empty).WithMessage("UnitId is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Document).Must(DocumentValidator.IsValidCpf).WithMessage("Invalid CPF.").WithErrorCode(ErrorCodes.Document.CpfInvalid);
        RuleFor(x => x.Address!).SetValidator(new AddressRequestValidator()).When(x => x.Address is not null);
    }
}
