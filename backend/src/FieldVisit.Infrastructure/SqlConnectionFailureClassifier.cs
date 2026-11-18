using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Observes existing EF SQL connection failures only. No new connections, tokens, queries,
/// retries, or changes to the HTTP response. Never log exception text or identities.
/// </summary>
public sealed class SqlConnectionFailureClassifier : DbConnectionInterceptor
{
    private readonly ILogger<SqlConnectionFailureClassifier> _logger;
    private long _lastLogTicks;
    private static readonly long MinLogIntervalTicks = TimeSpan.FromMinutes(1).Ticks;

    public SqlConnectionFailureClassifier(ILogger<SqlConnectionFailureClassifier> logger)
        => _logger = logger;

    public static string Category(int? sqlNumber) => sqlNumber switch
    {
        18456 => "SQL_AUTH_OR_DOSGUARD_UNKNOWN",
        4060 => "DATABASE_ACCESS_OR_UNAVAILABLE",
        53 or 11001 or 10060 or 258 => "NETWORK_OR_DNS_UNKNOWN",
        -2 => "TIMEOUT_UNKNOWN",
        _ => "SQL_CONNECTION_UNKNOWN"
    };

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
        => Record(eventData.Exception);

    public override Task ConnectionFailedAsync(
        DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Record(eventData.Exception);
        return Task.CompletedTask;
    }

    private void Record(Exception exception)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastLogTicks);
        if (now - last < MinLogIntervalTicks ||
            Interlocked.CompareExchange(ref _lastLogTicks, now, last) != last)
            return;

        // SqlException.Number 18456 alone does not distinguish missing principal,
        // invalid identity, permissions, or Azure SQL DoSGuard state 113.
        var category = Category((exception as SqlException)?.Number);
        _logger.LogWarning("PATHB_SQL_CONNECTION_FAILURE_CATEGORY={Category}", category);
    }
}
