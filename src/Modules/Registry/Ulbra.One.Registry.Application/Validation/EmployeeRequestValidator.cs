using FluentValidation;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Registry.Application.Validation;

public sealed class EmployeeRequestValidator : AbstractValidator<EmployeeRequest>
{
    public EmployeeRequestValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Document).Must(DocumentValidator.IsValidCpf).WithMessage("CPF inválido.");
    }
}
