using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ordinis.UnitTests")] // so tests can construct internal command/query handlers directly
[assembly: InternalsVisibleTo("Ordinis.IntegrationTests")]
