using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// Simulates the database being unreachable ("Given the database is unavailable"): while
/// <see cref="IsActive"/>, every attempt to open a connection fails the way an unreachable SQL
/// Server does. Switched off after every scenario (see <see cref="TestHost"/>).
/// </summary>
/// <remarks>
/// An interceptor rather than a second host with a bad connection string: it needs no extra
/// server, fails instantly instead of after a connection timeout, and is scoped to one scenario.
/// </remarks>
internal sealed class DatabaseOutage : DbConnectionInterceptor
{
    /// <summary>
    /// Connection details the failure message carries — as a real driver's message would — so a
    /// response leaking the exception is detectable.
    /// </summary>
    public const string ConnectionDetails = "Server=tcp:ordinis-db.internal,1433;User Id=ordinis_app;Password=s3cr3t-outage";

    /// <summary>Set while the test itself (not the API) uses the database — see <see cref="Bypass"/>.</summary>
    private static readonly AsyncLocal<bool> Bypassed = new();

    public bool IsActive { get; set; }

    /// <summary>
    /// Exempts the calling flow (the test's own seeding and token minting, which share the API's
    /// DbContext registration) from the outage until disposed: only the API is meant to lose its
    /// database.
    /// </summary>
    public static IDisposable Bypass()
    {
        bool previous = Bypassed.Value;
        Bypassed.Value = true;
        return new Restore(() => Bypassed.Value = previous);
    }

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ThrowIfActive();
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        ThrowIfActive();
        return ValueTask.FromResult(result);
    }

    private void ThrowIfActive()
    {
        if (IsActive && !Bypassed.Value)
        {
            throw new SimulatedOutageException(
                $"A network-related error occurred while establishing a connection to SQL Server ({ConnectionDetails}).");
        }
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    /// <summary>A provider-level failure, like the <c>SqlException</c> an unreachable server causes.</summary>
    private sealed class SimulatedOutageException(string message) : DbException(message);
}
