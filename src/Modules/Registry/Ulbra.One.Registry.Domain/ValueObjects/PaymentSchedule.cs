namespace Ulbra.One.Registry.Domain.ValueObjects;

/// <summary>How a vendor's installments are scheduled for payment, independent of the NF due date.</summary>
public enum PaymentScheduleMode { OnDueDate, FixedDayOfMonth, FirstDayOfMonth }

/// <summary>Per-vendor payment calendar. EF owned type. Resolves an installment's scheduled payment date.</summary>
public sealed record PaymentSchedule(PaymentScheduleMode Mode, int? DayOfMonth)
{
    public static PaymentSchedule Default => new(PaymentScheduleMode.OnDueDate, null);

    /// <summary>Resolves the scheduled payment date for a bill due on <paramref name="dueDate"/>.
    /// Never returns a date earlier than the due date (we don't pay before the bill exists).</summary>
    public DateOnly Resolve(DateOnly dueDate) => Mode switch
    {
        PaymentScheduleMode.OnDueDate => dueDate,
        PaymentScheduleMode.FixedDayOfMonth => FixedDay(dueDate, DayOfMonth ?? dueDate.Day),
        PaymentScheduleMode.FirstDayOfMonth => FirstDay(dueDate),
        _ => dueDate,
    };

    private static DateOnly FixedDay(DateOnly due, int day)
    {
        var candidate = OnDay(due.Year, due.Month, day);
        return candidate >= due ? candidate : OnDay(candidate.AddMonths(1).Year, candidate.AddMonths(1).Month, day);
    }

    private static DateOnly FirstDay(DateOnly due)
    {
        var firstThisMonth = new DateOnly(due.Year, due.Month, 1);
        return firstThisMonth >= due ? firstThisMonth : new DateOnly(due.AddMonths(1).Year, due.AddMonths(1).Month, 1);
    }

    private static DateOnly OnDay(int year, int month, int day)
    {
        var lastDay = DateTime.DaysInMonth(year, month);
        return new DateOnly(year, month, Math.Min(day, lastDay));
    }
}
