using FluentValidation;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Registry.Application.Validation;

public sealed class VendorRequestValidator : AbstractValidator<VendorRequest>
{
    public VendorRequestValidator()
    {
        RuleFor(x => x.CompanyId).Must(x => x.Value != Guid.Empty).WithMessage("CompanyId is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Document)
            .Must((req, doc) => req.PersonType == PersonType.Company
                ? DocumentValidator.IsValidCnpj(doc)
                : DocumentValidator.IsValidCpf(doc))
            .WithMessage("Invalid document (CPF/CNPJ).").WithErrorCode("Document.Invalid");
        RuleFor(x => x.Address!).SetValidator(new AddressRequestValidator()).When(x => x.Address is not null);
        When(x => x.PaymentScheduleMode == PaymentScheduleMode.FixedDayOfMonth, () =>
        {
            RuleFor(x => x.PaymentScheduleDay)
                .NotNull().InclusiveBetween(1, 28)
                .WithMessage("PaymentScheduleDay must be between 1 and 28 for FixedDayOfMonth.");
        });
        When(x => x.AcceptedPaymentMethods is not null, () =>
        {
            RuleForEach(x => x.AcceptedPaymentMethods!).IsInEnum().WithMessage("Invalid payment method.");
            RuleFor(x => x.AcceptedPaymentMethods!)
                .Must(m => m.Distinct().Count() == m.Count)
                .WithMessage("Duplicate payment methods are not allowed.");
        });
        When(x => x.WithholdingRates is not null, () =>
        {
            // Rate is a decimal fraction (0.015 = 1,5%); a retention rate is always within (0, 1].
            RuleForEach(x => x.WithholdingRates!).ChildRules(r =>
            {
                r.RuleFor(w => w.TaxType).NotEmpty().WithMessage("Withholding tax type is required.");
                r.RuleFor(w => w.Rate).GreaterThan(0m).LessThanOrEqualTo(1m)
                    .WithMessage("Withholding rate must be a fraction in (0, 1].");
            });
            RuleFor(x => x.WithholdingRates!)
                .Must(r => r.Select(w => w.TaxType).Distinct().Count() == r.Count)
                .WithMessage("Duplicate withholding tax types are not allowed.");
        });
    }
}
