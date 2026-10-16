using FluentValidation.TestHelper;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Registry.Application.Validation;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;
using Xunit;

namespace Ulbra.One.UnitTests.Registry;

public class VendorPaymentScheduleValidatorTests
{
    // Valid base request (Individual with valid CPF)
    private static VendorRequest ValidRequest() => new(
        PersonType.Individual, "Acme", null, "52998224725",
        null, null, null, null, null, TaxRegime.Unknown,
        PaymentScheduleMode.OnDueDate, null);

    [Fact]
    public void FixedDayOfMonth_requires_day_between_1_and_28()
    {
        var v = new VendorRequestValidator();
        var req = ValidRequest() with { PaymentScheduleMode = PaymentScheduleMode.FixedDayOfMonth, PaymentScheduleDay = 31 };
        v.TestValidate(req).ShouldHaveValidationErrorFor(x => x.PaymentScheduleDay);
    }

    [Fact]
    public void OnDueDate_ignores_day()
    {
        var v = new VendorRequestValidator();
        var req = ValidRequest() with { PaymentScheduleMode = PaymentScheduleMode.OnDueDate, PaymentScheduleDay = null };
        v.TestValidate(req).ShouldNotHaveValidationErrorFor(x => x.PaymentScheduleDay);
    }
}
