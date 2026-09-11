using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Narrow SQL Server translation for the concurrent notification insert race.
/// It never treats a generic duplicate, FK, check, or concurrency error as an
/// idempotent notification result.  Only an equivalent persisted MailOutbox
/// row can be detached and retried by the caller's transaction owner.
/// </summary>
public sealed class EfNotificationCollisionTranslator(AppDbContext db) : INotificationCollisionTranslator
{
    public async Task<bool> TryTranslateAsync(Exception exception, CancellationToken ct)
    {
        if (exception is not DbUpdateException updateException)
            return false;

        var sql = FindSqlException(updateException);
        if (sql is null)
            return false;

        var duplicateErrors = sql.Errors
            .Cast<SqlError>()
            .Where(x => x.Number is 2601 or 2627)
            .ToArray();
        if (duplicateErrors.Length == 0)
            return false;

        // A foreign-key/check violation or deadlock/concurrency error is never
        // a notification idempotency signal, even if SQL surfaces more
        // than one SQL error in the same exception.
        if (sql.Errors.Cast<SqlError>().Any(x => x.Number is 547 or 1205 or 3960))
            return false;

        var authorities = duplicateErrors
            .Select(ClassifyAuthority)
            .ToArray();

        // Every duplicate error in this exception must come from one of the
        // two frozen MailOutbox authorities.  A mixed/unknown duplicate is
        // deliberately not translated.
        if (authorities.Any(x => x is null))
            return false;

        var entries = updateException.Entries.ToArray();
        if (entries.Length == 0
            || entries.Any(x => x.Entity is not MailOutbox || x.State != EntityState.Added))
            return false;

        var trackedRows = entries
            .Select(x => (Entry: x, Row: (MailOutbox)x.Entity))
            .ToArray();
        var hasRecipientKeyAuthority = authorities.Any(x => x == CollisionAuthority.RecipientKey);
        var hasEmailAuthority = authorities.Any(x => x == CollisionAuthority.NormalizedRecipientEmail);
        var matchedRows = new List<(EntityEntry Entry, MailOutbox Row, bool RecipientKeyMatch, bool EmailMatch)>();

        foreach (var tracked in trackedRows)
        {
            var row = tracked.Row;
            var businessEventKey = row.BusinessEventKey?.Trim();
            if (string.IsNullOrWhiteSpace(businessEventKey)
                || !string.Equals(businessEventKey, row.BusinessEventKey, StringComparison.Ordinal)
                || businessEventKey.Length > 200)
                return false;
            if (!IsCanonicalRecipientKey(row.RecipientKey))
                return false;

            var normalizedEmail = NotificationEmailAuthority.NormalizeUsable(row.RecipientEmail);
            var query = db.Set<MailOutbox>().AsNoTracking()
                .Where(x => x.BusinessEventKey == businessEventKey);

            var match = await query.FirstOrDefaultAsync(
                x => x.RecipientKey == row.RecipientKey
                     && x.NormalizedRecipientEmail == normalizedEmail,
                ct);

            if (match is not null)
            {
                matchedRows.Add((
                    tracked.Entry,
                    row,
                    hasRecipientKeyAuthority,
                    hasEmailAuthority && normalizedEmail is not null));
            }
        }

        // A duplicate must have at least one durable equivalent competitor,
        // and each authority reported by SQL must be evidenced by one.  This
        // prevents a mismatched row or an unrelated unique failure from being
        // swallowed merely because another row in the same batch collided.
        if (matchedRows.Count == 0
            || authorities.Any(authority => !matchedRows.Any(x =>
                authority == CollisionAuthority.RecipientKey
                    ? x.RecipientKeyMatch
                    : x.EmailMatch)))
            return false;

        foreach (var matched in matchedRows)
            matched.Entry.State = EntityState.Detached;

        return true;
    }

    private static CollisionAuthority? ClassifyAuthority(SqlError error)
    {
        var message = error.Message;
        if (ContainsIdentifier(message, "UX_MailOutbox_BusinessEvent_RecipientKey"))
            return CollisionAuthority.RecipientKey;
        if (ContainsIdentifier(message, "UX_MailOutbox_BusinessEvent_NormalizedRecipientEmail"))
            return CollisionAuthority.NormalizedRecipientEmail;
        return null;
    }

    private static bool IsCanonicalRecipientKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var separator = value.IndexOf(':');
        if (separator != 3 && separator != 4)
            return false;

        var prefix = value[..separator];
        if (prefix != "EMP" && prefix != "USER")
            return false;

        if (!long.TryParse(value[(separator + 1)..], out var id) || id <= 0)
            return false;

        if (prefix == "USER" && id > int.MaxValue)
            return false;

        var canonical = prefix == "EMP"
            ? NotificationBusinessKeyAuthority.ForEmployment(id)
            : NotificationBusinessKeyAuthority.ForUser((int)id);
        return string.Equals(value, canonical, StringComparison.Ordinal);
    }

    private static bool ContainsIdentifier(string message, string identifier)
    {
        var at = message.IndexOf(identifier, StringComparison.Ordinal);
        if (at < 0)
            return false;

        static bool IsIdentifierChar(char value) => char.IsLetterOrDigit(value) || value == '_';
        var before = at == 0 || !IsIdentifierChar(message[at - 1]);
        var after = at + identifier.Length >= message.Length
                    || !IsIdentifierChar(message[at + identifier.Length]);
        return before && after;
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql)
                return sql;
        return null;
    }

    private enum CollisionAuthority
    {
        RecipientKey,
        NormalizedRecipientEmail
    }
}
