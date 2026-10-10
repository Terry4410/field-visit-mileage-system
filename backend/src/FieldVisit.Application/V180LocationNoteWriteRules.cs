namespace FieldVisit.Application;

/// <summary>
/// Location picker/search visibility never grants location note mutation.
/// The caller must revalidate effective TeamScope, Team, UserTeamAssignment
/// and TeamMembership in the database immediately before invoking this guard.
/// This is a personal-note permission only, not a Leader management grant.
/// </summary>
public static class V180LocationNoteWriteRules
{
    public static void RequireOwnTeamNote(
        int? actorOrganizationId, int? locationOrganizationId,
        int? locationTeamId, int requestedTeamId,
        int? locationCreatorId, int actorUserId, bool effectiveMember)
    {
        if (!actorOrganizationId.HasValue
            || !locationOrganizationId.HasValue
            || actorOrganizationId != locationOrganizationId
            || !locationTeamId.HasValue
            || locationTeamId.Value != requestedTeamId
            || locationCreatorId != actorUserId
            || !effectiveMember)
            throw new UnauthorizedAccessException(
                "B2_NOTE_WRITE_SCOPE_DENIED: 沒有目前有效的人員、小組、地點擁有權或備註維護權限。");
    }
}
