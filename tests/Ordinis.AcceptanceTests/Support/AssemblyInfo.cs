// Every scenario shares one API host and one database (reset after each scenario), so
// scenarios must run one at a time — Reqnroll generates one xUnit test class per feature, and
// xUnit would otherwise run those classes in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
