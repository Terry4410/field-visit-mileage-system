using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public static class V170AccessRules
{
    public static bool IsEffective(
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        DateOnly date)
        => effectiveFrom <= date
           && (!effectiveTo.HasValue || effectiveTo.Value >= date);

    /// <summary>
    /// Null means legacy user with no HR employment record yet.
    /// During migration that remains eligible so v1.6.1 accounts do not
    /// unexpectedly lose access before HR sync is introduced.
    /// </summary>
    public static bool IsEmploymentEligible(string? employmentStatus)
    {
        if (string.IsNullOrWhiteSpace(employmentStatus))
            return true;

        return string.Equals(
            employmentStatus,
            EmploymentStatuses.Active,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExternalAuthorizationEffective(
        DateOnly? authorizationFrom,
        DateOnly? authorizationTo,
        DateOnly date)
    {
        if (authorizationFrom.HasValue && authorizationFrom.Value > date)
            return false;

        if (authorizationTo.HasValue && authorizationTo.Value < date)
            return false;

        return true;
    }

    public static bool IsSystemAccessAllowed(
        bool adminEnabled,
        string? employmentStatus,
        string userType,
        DateOnly? authorizationFrom,
        DateOnly? authorizationTo,
        DateOnly date)
    {
        if (!adminEnabled)
            return false;

        if (string.Equals(
                userType,
                UserTypes.External,
                StringComparison.OrdinalIgnoreCase))
        {
            return IsExternalAuthorizationEffective(
                authorizationFrom,
                authorizationTo,
                date);
        }

        return IsEmploymentEligible(employmentStatus);
    }

    /// <summary>
    /// Supervisor is always read-only at business-operation level.
    /// Export is handled separately through explicit capabilities.
    /// </summary>
    public static bool CanMutateBusinessData(string activeRole)
        => !string.Equals(
            activeRole,
            "supervisor",
            StringComparison.OrdinalIgnoreCase);

    public static bool RequiresExplicitExportCapability(string activeRole)
        => string.Equals(
            activeRole,
            "supervisor",
            StringComparison.OrdinalIgnoreCase);
}
