using FieldVisit.Domain;
using FieldVisit.Domain.Entities;

namespace FieldVisit.Infrastructure;

public static class V180TripKeywordQuery
{
    // Called after authorization scoping and before Count/Skip/Take. Both UI
    // and exports use this same SQL-translatable predicate.
    public static IQueryable<VisitTrip> Apply(IQueryable<VisitTrip> trips,
        IQueryable<VisitTripSnapshot> latestSnapshots, IQueryable<User> users,
        IQueryable<Project> projects, IQueryable<VisitType> visitTypes, string? input)
    {
        var keyword = input?.Trim();
        if (string.IsNullOrEmpty(keyword)) return trips;
        return trips.Where(t => t.Status == TripStatuses.Approved
            ? latestSnapshots.Any(s => s.VisitTripId == t.VisitTripId && (
                s.TripNo.Contains(keyword) || s.EmployeeNoSnapshot.Contains(keyword) ||
                s.DisplayNameSnapshot.Contains(keyword) ||
                (s.TeamNameSnapshot != null && s.TeamNameSnapshot.Contains(keyword)) ||
                (s.NotesSnapshot != null && s.NotesSnapshot.Contains(keyword)) ||
                s.Stops.Any(st => st.LocationNameSnapshot.Contains(keyword) ||
                    (st.LocationCodeSnapshot != null && st.LocationCodeSnapshot.Contains(keyword)) ||
                    (st.AddressSnapshot != null && st.AddressSnapshot.Contains(keyword)) ||
                    (st.ProjectCodeSnapshot != null && st.ProjectCodeSnapshot.Contains(keyword)) ||
                    (st.ProjectNameSnapshot != null && st.ProjectNameSnapshot.Contains(keyword)) ||
                    (st.VisitTypeCodeSnapshot != null && st.VisitTypeCodeSnapshot.Contains(keyword)) ||
                    (st.VisitTypeNameSnapshot != null && st.VisitTypeNameSnapshot.Contains(keyword)) ||
                    (st.VisitPurposeSnapshot != null && st.VisitPurposeSnapshot.Contains(keyword)) ||
                    (st.NotesSnapshot != null && st.NotesSnapshot.Contains(keyword)))))
            : t.TripNo.Contains(keyword) ||
                (t.Purpose != null && t.Purpose.Contains(keyword)) ||
                (t.Notes != null && t.Notes.Contains(keyword)) ||
                users.Any(u => u.UserId == t.UserId && (
                    (u.EmployeeNo != null && u.EmployeeNo.Contains(keyword)) || u.DisplayName.Contains(keyword))) ||
                t.Stops.Any(st =>
                    (st.LocationNameSnapshot != null && st.LocationNameSnapshot.Contains(keyword)) ||
                    (st.AddressSnapshot != null && st.AddressSnapshot.Contains(keyword)) ||
                    (st.Location != null && st.Location.LocationCode != null && st.Location.LocationCode.Contains(keyword)) ||
                    projects.Any(p => p.ProjectId == st.ProjectId &&
                        (p.ProjectCode.Contains(keyword) || p.ProjectName.Contains(keyword))) ||
                    visitTypes.Any(v => v.VisitTypeId == st.VisitTypeId &&
                        (v.VisitTypeCode.Contains(keyword) || v.VisitTypeName.Contains(keyword))) ||
                    (st.VisitPurpose != null && st.VisitPurpose.Contains(keyword)) ||
                    (st.Notes != null && st.Notes.Contains(keyword))));
    }
}
