using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Aging;

/// <summary>Pure aging-bucket rule: classifies an installment due date relative to a reference date.</summary>
public static class AgingCalculator
{
    /// <summary>Days past due (0 when due today or in the future).</summary>
    public static int DaysOverdue(DateOnly dueDate, DateOnly asOf) => Math.Max(0, asOf.DayNumber - dueDate.DayNumber);

    public static AgingBucket BucketOf(DateOnly dueDate, DateOnly asOf)
    {
        var days = asOf.DayNumber - dueDate.DayNumber;
        if (days <= 0) return AgingBucket.NotYetDue;
        if (days <= 30) return AgingBucket.Overdue1To30;
        if (days <= 60) return AgingBucket.Overdue31To60;
        if (days <= 90) return AgingBucket.Overdue61To90;
        return AgingBucket.Overdue90Plus;
    }
}
