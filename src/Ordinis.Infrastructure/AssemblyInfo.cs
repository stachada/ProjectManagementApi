using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ordinis.IntegrationTests")] // so tests can drive internal background services (e.g. the webhook delivery dispatcher) directly
