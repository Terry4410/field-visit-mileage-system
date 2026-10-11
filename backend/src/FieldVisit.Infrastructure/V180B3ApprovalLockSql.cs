using System;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Parameterized, deterministic lock-order SQL for WORK-only B3 preview.
/// Never interpolate untrusted values into SQL text. This is NOT an apply
/// executor, and SQL Server catalog/migration approval remains separate.
/// </summary>
internal static class V180B3ApprovalLockSql
{
    internal static FormattableString RequestByPublicId(
        Guid publicId, int organizationId) =>
        $"""
        SELECT * FROM dbo.ChangeRequests WITH (UPDLOCK, HOLDLOCK)
        WHERE RequestPublicId = {publicId} AND OrganizationId = {organizationId}
        """;

    internal static FormattableString LocationById(
        int locationId, int organizationId) =>
        $"""
        SELECT * FROM dbo.Locations WITH (UPDLOCK, HOLDLOCK)
        WHERE LocationId = {locationId} AND OrganizationId = {organizationId}
        """;
}
