using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ordinis.UnitTests")] // so tests can construct internal command/query handlers directly
[assembly: InternalsVisibleTo("Ordinis.IntegrationTests")]
[assembly: InternalsVisibleTo("Ordinis.Benchmarks")] // so the Mapster comparison reuses TaskMapper.BuildLinks instead of a copy
