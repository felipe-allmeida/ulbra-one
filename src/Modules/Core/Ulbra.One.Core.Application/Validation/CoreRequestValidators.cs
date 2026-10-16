using FluentValidation;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Application.Endpoints.Companies.CreateCompany;
using Ulbra.One.Core.Application.Endpoints.Companies.UpdateCompany;
using Ulbra.One.Core.Application.Endpoints.CostCenters.CreateCostCenter;
using Ulbra.One.Core.Application.Endpoints.CostCenters.UpdateCostCenter;
using Ulbra.One.Core.Application.Endpoints.Units.CreateUnit;
using Ulbra.One.Core.Application.Endpoints.Units.UpdateUnit;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Core.Application.Validation;

/// <summary>
/// Field-shape rules for the Core write endpoints — required text, document format, non-negative money.
/// <para>
/// They exist because the Core aggregates take these values through plain setters: <c>Company.Update</c>
/// assigns <c>LegalName</c> and <c>Cnpj</c> without looking at them, so before this a company could be
/// created with an empty razão social and a CNPJ of "@@@", and the API answered 201.
/// </para>
/// <para>
/// Scope is deliberate: only what can be judged from the payload alone. Anything needing the database —
/// the company exists, the units belong to it, the cost center is active — stays in the handler, where it
/// already is and where it can produce the right 404 or 409 instead of a field error.
/// </para>
/// </summary>
internal static class CoreRules
{
    /// <summary>Required free text, rejecting whitespace-only as empty (the normalizer has already trimmed).</summary>
    internal static IRuleBuilderOptions<T, string> Required<T>(
        this IRuleBuilder<T, string> rule, string field, string errorCode) =>
        rule.NotEmpty().WithMessage($"{field} is required.").WithErrorCode(errorCode);
}

/// <summary>Rules for creating a company. The CNPJ is required here — a company without one cannot issue
/// or receive a fiscal document, so it is not the same as a unit's optional establishment CNPJ.</summary>
public sealed class CreateCompanyRequestValidator : AbstractValidator<CreateCompany.Request>
{
    public CreateCompanyRequestValidator()
    {
        RuleFor(x => x.LegalName).Required("LegalName", ErrorCodes.Company.LegalNameRequired);
        RuleFor(x => x.Cnpj)
            .Must(cnpj => DocumentValidator.IsValidCnpj(cnpj))
            .WithMessage("Invalid CNPJ.").WithErrorCode(ErrorCodes.Company.CnpjInvalid);
    }
}

/// <summary>Same rules as creating — an update replaces both fields, so it can corrupt a company just as
/// easily as a create can produce a broken one.</summary>
public sealed class UpdateCompanyRequestValidator : AbstractValidator<UpdateCompany.Request>
{
    public UpdateCompanyRequestValidator()
    {
        RuleFor(x => x.LegalName).Required("LegalName", ErrorCodes.Company.LegalNameRequired);
        RuleFor(x => x.Cnpj)
            .Must(cnpj => DocumentValidator.IsValidCnpj(cnpj))
            .WithMessage("Invalid CNPJ.").WithErrorCode(ErrorCodes.Company.CnpjInvalid);
    }
}

/// <summary>Rules for creating a unit. The CNPJ is optional (it exists for accounting tracking), but a
/// value that is present has to be a real one.</summary>
public sealed class CreateUnitRequestValidator : AbstractValidator<CreateUnit.Request>
{
    public CreateUnitRequestValidator()
    {
        RuleFor(x => x.Name).Required("Name", ErrorCodes.Unit.NameRequired);
        RuleFor(x => x.Acronym).Required("Acronym", ErrorCodes.Unit.AcronymRequired);
        RuleFor(x => x.Cnpj!)
            .Must(cnpj => DocumentValidator.IsValidCnpj(cnpj))
            .When(x => !string.IsNullOrWhiteSpace(x.Cnpj))
            .WithMessage("Invalid CNPJ.").WithErrorCode(ErrorCodes.Unit.CnpjInvalid);
    }
}

/// <summary>Rules for updating a unit — identical to creating one.</summary>
public sealed class UpdateUnitRequestValidator : AbstractValidator<UpdateUnit.Request>
{
    public UpdateUnitRequestValidator()
    {
        RuleFor(x => x.Name).Required("Name", ErrorCodes.Unit.NameRequired);
        RuleFor(x => x.Acronym).Required("Acronym", ErrorCodes.Unit.AcronymRequired);
        RuleFor(x => x.Cnpj!)
            .Must(cnpj => DocumentValidator.IsValidCnpj(cnpj))
            .When(x => !string.IsNullOrWhiteSpace(x.Cnpj))
            .WithMessage("Invalid CNPJ.").WithErrorCode(ErrorCodes.Unit.CnpjInvalid);
    }
}

/// <summary>Rules for creating a cost center. A negative auto-approval limit is rejected because the limit
/// feeds the rule that approves payables without human review — a nonsensical value there is not cosmetic.</summary>
public sealed class CreateCostCenterRequestValidator : AbstractValidator<CreateCostCenter.Request>
{
    public CreateCostCenterRequestValidator()
    {
        RuleFor(x => x.Code).Required("Code", ErrorCodes.CostCenter.CodeRequired);
        RuleFor(x => x.Name).Required("Name", ErrorCodes.CostCenter.NameRequired);
        RuleFor(x => x.AutoApprovalLimit).GreaterThanOrEqualTo(0)
            .WithMessage("AutoApprovalLimit cannot be negative.")
            .WithErrorCode(ErrorCodes.CostCenter.AutoApprovalLimitInvalid);
    }
}

/// <summary>Rules for updating a cost center — identical to creating one.</summary>
public sealed class UpdateCostCenterRequestValidator : AbstractValidator<UpdateCostCenter.Request>
{
    public UpdateCostCenterRequestValidator()
    {
        RuleFor(x => x.Code).Required("Code", ErrorCodes.CostCenter.CodeRequired);
        RuleFor(x => x.Name).Required("Name", ErrorCodes.CostCenter.NameRequired);
        RuleFor(x => x.AutoApprovalLimit).GreaterThanOrEqualTo(0)
            .WithMessage("AutoApprovalLimit cannot be negative.")
            .WithErrorCode(ErrorCodes.CostCenter.AutoApprovalLimitInvalid);
    }
}
