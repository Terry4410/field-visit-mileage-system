namespace FieldVisit.Application;

/// <summary>
/// Permanent deletion must preserve ALL historical references, including
/// snapshots and audit-bearing metadata. The first five counters keep the
/// pre-existing public impact DTO layout for backwards compatibility.
/// This rule is not a permission grant.
/// </summary>
public static class V180ManagedLocationDeletionRules
{
    public static bool CanPermanentlyDelete(
        int tripStops,int projects,int favorites,int approvalHistory,
        int governmentMatches,int snapshotStops,int teamNotes,
        int teamNoteHistory,int geocodingHistory,int deploymentHistory,
        int mergedLocationReferences)
    {
        return new[]{tripStops,projects,favorites,approvalHistory,
            governmentMatches,snapshotStops,teamNotes,teamNoteHistory,
            geocodingHistory,deploymentHistory,mergedLocationReferences}
            .All(count=>count==0);
    }
}
