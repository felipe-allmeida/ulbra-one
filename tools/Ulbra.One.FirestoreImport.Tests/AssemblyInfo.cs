// The importer is a single sequential writer. Disable xUnit's default cross-class parallelization so the
// live smoke tests (preflight / dry-run / apply) never overlap on the same database when run together.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
