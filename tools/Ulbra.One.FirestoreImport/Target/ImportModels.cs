namespace Ulbra.One.FirestoreImport.Target;

/// <summary>What happened to a single source document.</summary>
public enum RowOutcome { Inserted, Updated, Skipped, DeadLettered }

/// <summary>Running tally for one collection's import.</summary>
public sealed class ImportCounters
{
    public int Read { get; private set; }
    public int Inserted { get; private set; }
    public int Updated { get; private set; }
    public int Skipped { get; private set; }
    public int DeadLettered { get; private set; }

    public void CountRead() => Read++;

    public void Count(RowOutcome outcome)
    {
        switch (outcome)
        {
            case RowOutcome.Inserted: Inserted++; break;
            case RowOutcome.Updated: Updated++; break;
            case RowOutcome.Skipped: Skipped++; break;
            case RowOutcome.DeadLettered: DeadLettered++; break;
        }
    }

    public ImportSummary ToSummary(string entity, bool dryRun) =>
        new(entity, dryRun, Read, Inserted, Updated, Skipped, DeadLettered);
}

/// <summary>The result returned to the caller for one collection.</summary>
public sealed record ImportSummary(
    string Entity, bool DryRun, int Read, int Inserted, int Updated, int Skipped, int DeadLettered);
