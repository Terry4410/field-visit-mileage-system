using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V160FinalRepository(AppDbContext db) : IV160FinalRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult<TripQueryRowDto>> QueryTripsAsync(CurrentUserDto user, TripQueryRequest request, bool exportAll, CancellationToken ct)
    {
        var latestSnapshotQ = db.VisitTripSnapshots.AsNoTracking().Where(s =>
            !db.VisitTripSnapshots.Any(newer => newer.VisitTripId == s.VisitTripId && newer.SnapshotVersion > s.SnapshotVersion));

        IQueryable<VisitTrip> q = ApplyTripScope(db.VisitTrips.AsNoTracking(), user);
        if (!request.IncludeCancelled) q = q.Where(x => x.Status != TripStatuses.Cancelled);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            q = q.Where(x => x.Status == status);
        }
        if (request.StartDate.HasValue)
        {
            var start = request.StartDate.Value;
            q = q.Where(t => t.Status == TripStatuses.Approved
                ? latestSnapshotQ.Any(s => s.VisitTripId == t.VisitTripId && s.VisitDate >= start)
                : t.VisitDate >= start);
        }
        if (request.EndDate.HasValue)
        {
            var end = request.EndDate.Value;
            q = q.Where(t => t.Status == TripStatuses.Approved
                ? latestSnapshotQ.Any(s => s.VisitTripId == t.VisitTripId && s.VisitDate <= end)
                : t.VisitDate <= end);
        }
        if (request.TeamId.HasValue)
        {
            var teamId = request.TeamId.Value;
            q = q.Where(t => t.Status == TripStatuses.Approved
                ? latestSnapshotQ.Any(s => s.VisitTripId == t.VisitTripId && s.TeamId == teamId)
                : t.TeamId == teamId);
        }
        if (request.VisitorId.HasValue)
        {
            var visitorId = request.VisitorId.Value;
            q = q.Where(t => t.UserId == visitorId);
        }
        if (!string.IsNullOrWhiteSpace(request.LocationKeyword))
        {
            var keyword = request.LocationKeyword.Trim();
            q = q.Where(t => t.Status == TripStatuses.Approved
                ? latestSnapshotQ.Any(s => s.VisitTripId == t.VisitTripId && s.Stops.Any(st =>
                    st.LocationNameSnapshot.Contains(keyword) ||
                    (st.AddressSnapshot != null && st.AddressSnapshot.Contains(keyword)) ||
                    (st.LocationCodeSnapshot != null && st.LocationCodeSnapshot.Contains(keyword))))
                : t.Stops.Any(st =>
                    (st.LocationNameSnapshot != null && st.LocationNameSnapshot.Contains(keyword)) ||
                    (st.AddressSnapshot != null && st.AddressSnapshot.Contains(keyword)) ||
                    (st.Location != null && st.Location.LocationCode != null && st.Location.LocationCode.Contains(keyword))));
        }
        if (request.ProjectId.HasValue)
        {
            var projectId = request.ProjectId.Value;
            q = q.Where(t => t.Status == TripStatuses.Approved
                ? latestSnapshotQ.Any(s => s.VisitTripId == t.VisitTripId && s.Stops.Any(st => st.ProjectId == projectId))
                : t.Stops.Any(st => st.ProjectId == projectId));
        }
        if (request.VisitTypeId.HasValue)
        {
            var visitTypeId = request.VisitTypeId.Value;
            q = q.Where(t => t.Status == TripStatuses.Approved
                ? latestSnapshotQ.Any(s => s.VisitTripId == t.VisitTripId && s.Stops.Any(st => st.VisitTypeId == visitTypeId))
                : t.Stops.Any(st => st.VisitTypeId == visitTypeId));
        }

        var candidate = q.Select(t => new
        {
            t.VisitTripId,
            t.TripNo,
            EffectiveDate = t.Status == TripStatuses.Approved
                ? latestSnapshotQ.Where(s => s.VisitTripId == t.VisitTripId).Select(s => (DateOnly?)s.VisitDate).FirstOrDefault() ?? t.VisitDate
                : t.VisitDate,
            EffectiveVisitorName = t.Status == TripStatuses.Approved
                ? latestSnapshotQ.Where(s => s.VisitTripId == t.VisitTripId).Select(s => s.DisplayNameSnapshot).FirstOrDefault()
                : db.Users.Where(u => u.UserId == t.UserId).Select(u => u.DisplayName).FirstOrDefault()
        });

        var total = await candidate.CountAsync(ct);
        if (request.Sort.Equals("date_asc", StringComparison.OrdinalIgnoreCase))
            candidate = candidate.OrderBy(x => x.EffectiveDate).ThenBy(x => x.TripNo);
        else if (request.Sort.Equals("visitor_asc", StringComparison.OrdinalIgnoreCase))
            candidate = candidate.OrderBy(x => x.EffectiveVisitorName).ThenByDescending(x => x.EffectiveDate).ThenBy(x => x.TripNo);
        else
            candidate = candidate.OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.TripNo);

        var idQuery = candidate.Select(x => x.VisitTripId);
        if (!exportAll) idQuery = idQuery.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);
        var selectedIds = await idQuery.ToListAsync(ct);
        if (selectedIds.Count == 0)
            return new PagedResult<TripQueryRowDto>([], exportAll ? 1 : request.Page, exportAll ? Math.Max(total, 1) : request.PageSize, total);

        var trips = await db.VisitTrips.AsNoTracking().Include(x => x.Stops).ThenInclude(x => x.Location).Include(x => x.MileageCalculation)
            .Where(x => selectedIds.Contains(x.VisitTripId)).ToListAsync(ct);
        var tripIds = trips.Select(x => x.VisitTripId).ToList();
        var userIds = trips.Select(x => x.UserId).Distinct().ToList();
        var teamIds = trips.Where(x => x.TeamId.HasValue).Select(x => x.TeamId!.Value).Distinct().ToList();
        var projectIds = trips.SelectMany(x => x.Stops).Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToList();
        var visitTypeIds = trips.SelectMany(x => x.Stops).Where(x => x.VisitTypeId.HasValue).Select(x => x.VisitTypeId!.Value).Distinct().ToList();
        var locationIds = trips.SelectMany(x => x.Stops).Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().ToList();

        var profiles = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, ct);
        var teams = await db.Teams.AsNoTracking().Where(x => teamIds.Contains(x.TeamId)).ToDictionaryAsync(x => x.TeamId, ct);
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToDictionaryAsync(x => x.ProjectId, ct);
        var visitTypes = await db.VisitTypes.AsNoTracking().Where(x => visitTypeIds.Contains(x.VisitTypeId)).ToDictionaryAsync(x => x.VisitTypeId, ct);
        var locations = await db.Locations.AsNoTracking().Where(x => locationIds.Contains(x.LocationId)).ToDictionaryAsync(x => x.LocationId, ct);

        var snapshots = await db.VisitTripSnapshots.AsNoTracking().Include(x => x.Stops)
            .Where(x => tripIds.Contains(x.VisitTripId)).OrderByDescending(x => x.SnapshotVersion).ToListAsync(ct);
        var latestSnapshots = snapshots.GroupBy(x => x.VisitTripId).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.SnapshotVersion).First());
        var corrections = await db.CorrectionRequests.AsNoTracking().Where(x => tripIds.Contains(x.VisitTripId))
            .OrderByDescending(x => x.CorrectionRequestId).ToListAsync(ct);
        var correctionStatus = corrections.GroupBy(x => x.VisitTripId).ToDictionary(x => x.Key, x => x.First().Status);

        var rows = new Dictionary<long, TripQueryRowDto>();
        foreach (var trip in trips)
        {
            latestSnapshots.TryGetValue(trip.VisitTripId, out var snapshot);
            profiles.TryGetValue(trip.UserId, out var profile);
            var team = trip.TeamId.HasValue && teams.TryGetValue(trip.TeamId.Value, out var resolvedTeam) ? resolvedTeam : null;
            correctionStatus.TryGetValue(trip.VisitTripId, out var correction);

            if (trip.Status == TripStatuses.Approved)
            {
                if (snapshot is null) throw new InvalidOperationException($"已核准行程 {trip.TripNo} 缺少 Snapshot；請先執行 v1.6.0 Migration Verify。");
                var stops = snapshot.Stops.OrderBy(x => x.StopSequence).Select(x => new QueryStopDto(
                    x.StopSequence, x.LocationId, x.LocationCodeSnapshot, x.LocationNameSnapshot, x.AddressSnapshot,
                    x.ProjectId, x.ProjectCodeSnapshot, x.ProjectNameSnapshot, x.VisitTypeId, x.VisitTypeCodeSnapshot,
                    x.VisitTypeNameSnapshot, x.VisitPurposeSnapshot, x.NotesSnapshot)).ToList();
                rows[trip.VisitTripId] = new TripQueryRowDto(
                    trip.VisitTripId, snapshot.TripNo, snapshot.VisitDate, snapshot.StartTime, snapshot.EndTime,
                    snapshot.UserId, snapshot.EmployeeNoSnapshot, snapshot.DisplayNameSnapshot,
                    snapshot.TeamId, snapshot.TeamNameSnapshot, string.Join(" → ", stops.Select(x => x.LocationName)),
                    JoinDistinct(stops.Select(x => x.ProjectName)), JoinDistinct(stops.Select(x => x.VisitTypeName)),
                    snapshot.ClaimedDistanceKmSnapshot, snapshot.SystemDistanceKmSnapshot, snapshot.ApprovedDistanceKmSnapshot,
                    snapshot.RatePerKmSnapshot, snapshot.SubsidyAmountSnapshot,
                    stops.Count < 2 ? "NotApplicable" : snapshot.SystemDistanceKmSnapshot.HasValue ? "Calculated" : "Pending",
                    TripStatuses.Approved, TripStatuses.Display(TripStatuses.Approved), snapshot.SnapshotVersion, true,
                    snapshot.NotesSnapshot, correction, stops);
            }
            else
            {
                var stops = trip.Stops.OrderBy(x => x.StopSequence).Select(x =>
                {
                    var project = x.ProjectId.HasValue && projects.TryGetValue(x.ProjectId.Value, out var resolvedProject) ? resolvedProject : null;
                    var visitType = x.VisitTypeId.HasValue && visitTypes.TryGetValue(x.VisitTypeId.Value, out var resolvedVisitType) ? resolvedVisitType : null;
                    var location = x.LocationId.HasValue && locations.TryGetValue(x.LocationId.Value, out var resolvedLocation) ? resolvedLocation : null;
                    return new QueryStopDto(
                        x.StopSequence, x.LocationId, location?.LocationCode, x.LocationNameSnapshot ?? location?.LocationName ?? "",
                        x.AddressSnapshot ?? location?.Address ?? location?.PlusCode, x.ProjectId, project?.ProjectCode, project?.ProjectName,
                        x.VisitTypeId, visitType?.VisitTypeCode, visitType?.VisitTypeName, x.VisitPurpose, x.Notes);
                }).ToList();
                var calc = trip.MileageCalculation;
                rows[trip.VisitTripId] = new TripQueryRowDto(
                    trip.VisitTripId, trip.TripNo, trip.VisitDate, trip.StartTime, trip.EndTime, trip.UserId,
                    profile?.EmployeeNo ?? "", profile?.DisplayName ?? $"User {trip.UserId}", trip.TeamId, team?.TeamName,
                    string.Join(" → ", stops.Select(x => x.LocationName)), JoinDistinct(stops.Select(x => x.ProjectName)),
                    JoinDistinct(stops.Select(x => x.VisitTypeName)), calc?.ClaimedDistanceKm, calc?.SystemDistanceKm,
                    calc?.ApprovedDistanceKm, calc?.RatePerKmSnapshot, calc?.ApprovedAmount,
                    stops.Count < 2 ? "NotApplicable" : calc?.SystemDistanceKm.HasValue == true ? "Calculated" : "Pending",
                    trip.Status, TripStatuses.Display(trip.Status), 0, false, trip.Notes, correction, stops);
            }
        }

        var orderedRows = selectedIds.Where(rows.ContainsKey).Select(id => rows[id]).ToList();
        return new PagedResult<TripQueryRowDto>(orderedRows, exportAll ? 1 : request.Page, exportAll ? Math.Max(total, 1) : request.PageSize, total);
    }

    public async Task<CorrectionDraftDto> GetCorrectionDraftAsync(CurrentUserDto user, long visitTripId, CancellationToken ct)
    {
        var trip = await db.VisitTrips.AsNoTracking().FirstOrDefaultAsync(x => x.VisitTripId == visitTripId, ct)
            ?? throw new KeyNotFoundException("找不到行程。");
        if (trip.UserId != user.UserId) throw new UnauthorizedAccessException("只能更正自己的行程。");
        if (trip.Status != TripStatuses.Approved) throw new InvalidOperationException("只有已核准行程可以提出更正。");
        var snapshot = await GetLatestSnapshotAsync(visitTripId, ct) ?? throw new InvalidOperationException("找不到核准 Snapshot，請聯絡管理者。");
        return new CorrectionDraftDto(visitTripId, snapshot.TripNo, snapshot.SnapshotVersion, ProposalFrom(snapshot));
    }

    public async Task<CorrectionRequestDto> CreateCorrectionAsync(CurrentUserDto user, CreateCorrectionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("更正原因必填。");
        if (request.Proposal.Stops.Count < 1) throw new InvalidOperationException("更正後至少需要一個公務地點。");
        if (request.Proposal.StartTime.HasValue && request.Proposal.EndTime.HasValue && request.Proposal.EndTime <= request.Proposal.StartTime) throw new InvalidOperationException("更正後結束時間必須晚於開始時間。");
        if (request.Proposal.ClaimedDistanceKm is < 0 || request.Proposal.ApprovedDistanceKm is < 0) throw new InvalidOperationException("里程不可小於 0。");
        if (request.Proposal.Stops.Any(x => string.IsNullOrWhiteSpace(x.LocationName))) throw new InvalidOperationException("更正後地點名稱不可空白。");
        var trip = await db.VisitTrips.AsNoTracking().FirstOrDefaultAsync(x => x.VisitTripId == request.VisitTripId, ct)
            ?? throw new KeyNotFoundException("找不到行程。");
        if (trip.UserId != user.UserId) throw new UnauthorizedAccessException("只能更正自己的行程。");
        if (trip.Status != TripStatuses.Approved) throw new InvalidOperationException("只有已核准行程可以提出更正。");
        if (await db.CorrectionRequests.AnyAsync(x => x.VisitTripId == request.VisitTripId && (x.Status == "PendingLeaderReview" || x.Status == "PendingAdminClose"), ct))
            throw new InvalidOperationException("此行程已有待處理的更正申請。");

        var snapshot = await GetLatestSnapshotAsync(request.VisitTripId, ct) ?? throw new InvalidOperationException("找不到核准 Snapshot。");
        // Policy values are server-owned. Recalculate the applicable rate from the corrected business date;
        // a one-stop correction remains NotApplicable. This also guarantees that a date correction which crosses
        // a rate boundary becomes a financial correction and therefore requires Admin close.
        var correctedHasMileage = request.Proposal.Stops.Count >= 2;
        decimal? correctedRate = null;
        if (correctedHasMileage)
        {
            if (request.Proposal.ClaimedDistanceKm is null or <= 0) throw new InvalidOperationException("兩個以上地點的更正必須填寫自算里程。");
            if (request.Proposal.ApprovedDistanceKm is null or < 0) throw new InvalidOperationException("兩個以上地點的更正必須填寫核定里程。");
            correctedRate = await db.MileageRateRules.AsNoTracking()
                .Where(x => x.IsActive && (x.OrganizationId == trip.OrganizationId || x.OrganizationId == null) &&
                    x.VehicleType == (snapshot.VehicleTypeSnapshot ?? "Motorcycle") && x.EffectiveFrom <= request.Proposal.VisitDate &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.Proposal.VisitDate))
                .OrderByDescending(x => x.OrganizationId.HasValue).ThenByDescending(x => x.EffectiveFrom)
                .Select(x => (decimal?)x.RatePerKm).FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("找不到更正日期適用的補助費率。");
        }
        var normalizedProposal = request.Proposal with
        {
            ClaimedDistanceKm = correctedHasMileage ? request.Proposal.ClaimedDistanceKm : null,
            ApprovedDistanceKm = correctedHasMileage ? request.Proposal.ApprovedDistanceKm : null,
            RatePerKm = correctedRate,
            SubsidyAmount = correctedHasMileage && request.Proposal.ApprovedDistanceKm.HasValue && correctedRate.HasValue
                ? decimal.Round(request.Proposal.ApprovedDistanceKm.Value * correctedRate.Value, 2)
                : null
        };
        var changes = Diff(snapshot, normalizedProposal);
        if (changes.Count == 0) throw new InvalidOperationException("更正內容與目前核准資料相同。");

        var row = new CorrectionRequest
        {
            VisitTripId = request.VisitTripId,
            BaseSnapshotId = snapshot.VisitTripSnapshotId,
            Status = "PendingLeaderReview",
            Reason = request.Reason.Trim(),
            ProposedChangesJson = JsonSerializer.Serialize(normalizedProposal, JsonOptions),
            RequestedByUserId = user.UserId,
            RequestedAt = DateTime.UtcNow
        };
        await db.CorrectionRequests.AddAsync(row, ct);
        await db.SaveChangesAsync(ct);
        foreach (var change in changes)
        {
            await db.CorrectionRequestChanges.AddAsync(new CorrectionRequestChange
            {
                CorrectionRequestId = row.CorrectionRequestId,
                FieldName = change.FieldName,
                OldValue = change.OldValue,
                NewValue = change.NewValue,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }
        AddAudit(user.UserId, "CorrectionRequest", row.CorrectionRequestId.ToString(), "CorrectionRequested", new { request.VisitTripId, request.Reason, Changes = changes.Count });
        await db.SaveChangesAsync(ct);
        return await MapCorrectionAsync(row.CorrectionRequestId, ct);
    }

    public async Task<IReadOnlyList<CorrectionRequestDto>> GetCorrectionsAsync(CurrentUserDto user, string? status, CancellationToken ct)
    {
        var q = db.CorrectionRequests.AsNoTracking().AsQueryable();
        if (HasRole(user, "admin") || HasRole(user, "supervisor"))
        {
            var orgId = user.OrganizationId ?? -1;
            q = q.Where(x => db.VisitTrips.Any(t => t.VisitTripId == x.VisitTripId && t.OrganizationId == orgId));
        }
        else if (HasRole(user, "leader"))
        {
            var teamIds = user.TeamIds;
            q = q.Where(x => db.VisitTrips.Any(t => t.VisitTripId == x.VisitTripId && t.TeamId.HasValue && teamIds.Contains(t.TeamId.Value)));
        }
        else if (HasRole(user, "visitor")) q = q.Where(x => x.RequestedByUserId == user.UserId);
        else q = q.Where(x => false);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        var ids = await q.OrderByDescending(x => x.RequestedAt).Select(x => x.CorrectionRequestId).ToListAsync(ct);
        var result = new List<CorrectionRequestDto>(ids.Count);
        foreach (var id in ids) result.Add(await MapCorrectionAsync(id, ct));
        return result;
    }

    public async Task<CorrectionRequestDto> ReviewCorrectionAsync(CurrentUserDto user, long correctionRequestId, ReviewCorrectionRequest request, CancellationToken ct)
    {
        var row = await db.CorrectionRequests.FirstOrDefaultAsync(x => x.CorrectionRequestId == correctionRequestId, ct)
            ?? throw new KeyNotFoundException("找不到更正申請。");
        if (row.Status != "PendingLeaderReview") throw new InvalidOperationException("此更正申請目前不可由小組長審核。");
        EnsureRowVersion(row.RowVersion, request.RowVersion);
        var trip = await db.VisitTrips.AsNoTracking().FirstAsync(x => x.VisitTripId == row.VisitTripId, ct);
        if (!trip.TeamId.HasValue || !user.TeamIds.Contains(trip.TeamId.Value)) throw new UnauthorizedAccessException("無權審核未授權小組資料。");

        row.LeaderReviewedByUserId = user.UserId;
        row.LeaderReviewedAt = DateTime.UtcNow;
        row.LeaderComments = request.Comments;
        if (!request.Approve)
        {
            row.Status = "Rejected";
        }
        else
        {
            var changes = await db.CorrectionRequestChanges.AsNoTracking().Where(x => x.CorrectionRequestId == correctionRequestId).ToListAsync(ct);
            var requiresAdmin = RequiresAdminClose(changes);
            if (requiresAdmin) row.Status = "PendingAdminClose";
            else
            {
                var snapshot = await CreateCorrectionSnapshotAsync(row, user.UserId, ct);
                row.ResultSnapshotId = snapshot.VisitTripSnapshotId;
                row.Status = "Closed";
            }
        }
        AddAudit(user.UserId, "CorrectionRequest", row.CorrectionRequestId.ToString(), request.Approve ? "CorrectionLeaderApproved" : "CorrectionLeaderRejected", new { request.Comments, row.Status });
        await db.SaveChangesAsync(ct);
        return await MapCorrectionAsync(row.CorrectionRequestId, ct);
    }

    public async Task<CorrectionRequestDto> CloseCorrectionAsync(CurrentUserDto user, long correctionRequestId, CloseCorrectionRequest request, CancellationToken ct)
    {
        var row = await db.CorrectionRequests.FirstOrDefaultAsync(x => x.CorrectionRequestId == correctionRequestId, ct)
            ?? throw new KeyNotFoundException("找不到更正申請。");
        if (row.Status != "PendingAdminClose") throw new InvalidOperationException("此更正申請目前不需要管理者結案。");
        EnsureRowVersion(row.RowVersion, request.RowVersion);
        var trip = await db.VisitTrips.AsNoTracking().FirstAsync(x => x.VisitTripId == row.VisitTripId, ct);
        if (user.OrganizationId.HasValue && trip.OrganizationId != user.OrganizationId.Value) throw new UnauthorizedAccessException("無權處理其他 Organization 資料。");

        row.AdminClosedByUserId = user.UserId;
        row.AdminClosedAt = DateTime.UtcNow;
        row.AdminComments = request.Comments;
        if (!request.Approve) row.Status = "Rejected";
        else
        {
            var snapshot = await CreateCorrectionSnapshotAsync(row, user.UserId, ct);
            row.ResultSnapshotId = snapshot.VisitTripSnapshotId;
            row.Status = "Closed";
        }
        AddAudit(user.UserId, "CorrectionRequest", row.CorrectionRequestId.ToString(), request.Approve ? "CorrectionAdminClosed" : "CorrectionAdminRejected", new { request.Comments, row.Status });
        await db.SaveChangesAsync(ct);
        return await MapCorrectionAsync(row.CorrectionRequestId, ct);
    }

    public async Task<IReadOnlyList<UserOptionDto>> GetScopedVisitorsAsync(CurrentUserDto user, CancellationToken ct)
    {
        var q = db.Users.AsNoTracking().Where(x => x.IsActive);
        if (HasRole(user, "admin") || HasRole(user, "supervisor"))
        {
            if (!user.OrganizationId.HasValue) return [];
            q = q.Where(x => x.OrganizationId == user.OrganizationId.Value);
        }
        else if (HasRole(user, "leader"))
        {
            var teamIds = user.TeamIds;
            q = teamIds.Count == 0 ? q.Where(x => false) : q.Where(x => x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value));
        }
        else if (HasRole(user, "visitor")) q = q.Where(x => x.UserId == user.UserId);
        else q = q.Where(x => false);

        var rows = await q.OrderBy(x => x.DisplayName).ToListAsync(ct);
        var teamIds2 = rows.Where(x => x.TeamId.HasValue).Select(x => x.TeamId!.Value).Distinct().ToList();
        var teams = await db.Teams.AsNoTracking().Where(x => teamIds2.Contains(x.TeamId)).ToDictionaryAsync(x => x.TeamId, ct);
        return rows.Select(x => new UserOptionDto(x.UserId, x.EmployeeNo, x.DisplayName, x.TeamId,
            x.TeamId.HasValue && teams.TryGetValue(x.TeamId.Value, out var t) ? t.TeamName : null)).ToList();
    }

    public async Task<IReadOnlyList<AdminUserAccessDto>> GetUsersAsync(CurrentUserDto user, CancellationToken ct)
    {
        var orgId = RequireOrganization(user);
        var users = await db.Users.AsNoTracking().Where(x => x.OrganizationId == orgId).OrderBy(x => x.EmployeeNo).ToListAsync(ct);
        var ids = users.Select(x => x.UserId).ToList();
        var roles = await (from ur in db.UserRoles.AsNoTracking() join r in db.Roles.AsNoTracking() on ur.RoleId equals r.RoleId where ids.Contains(ur.UserId) select new { ur.UserId, r.RoleCode }).ToListAsync(ct);
        var scopes = await (from s in db.UserTeamScopes.AsNoTracking() join t in db.Teams.AsNoTracking() on s.TeamId equals t.TeamId where ids.Contains(s.UserId) && s.IsActive select new { s.UserId, Dto = new TeamScopeDto(t.TeamId, t.TeamName, s.IsPrimary) }).ToListAsync(ct);
        return users.Select(x => new AdminUserAccessDto(
            x.UserId, x.EmployeeNo, x.DisplayName, x.Email, x.IsActive,
            roles.Where(r => r.UserId == x.UserId).Select(r => NormalizeRole(r.RoleCode)).Distinct().OrderBy(r => r).ToList(),
            scopes.Where(s => s.UserId == x.UserId).Select(s => s.Dto).OrderByDescending(s => s.IsPrimary).ThenBy(s => s.TeamName).ToList())).ToList();
    }

    public async Task<AdminUserAccessDto> SaveUserAccessAsync(CurrentUserDto user, int userId, SaveUserAccessRequest request, CancellationToken ct)
    {
        var orgId = RequireOrganization(user);
        var allowedRoles = new HashSet<string>(new[] { "visitor", "leader", "admin", "supervisor" }, StringComparer.OrdinalIgnoreCase);
        var requestedRoles = request.Roles.Select(NormalizeRole).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requestedRoles.Any(x => !allowedRoles.Contains(x))) throw new InvalidOperationException("包含不允許的角色。");
        if (requestedRoles.Contains("leader") && request.TeamScopes.Count == 0) throw new InvalidOperationException("小組長至少需要一個管理小組。");
        if (request.TeamScopes.Count(x => x.IsPrimary) > 1) throw new InvalidOperationException("只能設定一個主要小組。");
        if (request.TeamScopes.Count > 0 && request.TeamScopes.All(x => !x.IsPrimary)) throw new InvalidOperationException("有小組授權時必須指定一個主要小組。");
        var requestedTeamIds = request.TeamScopes.Select(x => x.TeamId).Distinct().ToList();
        var validTeamIds = await db.Teams.AsNoTracking().Where(x => x.OrganizationId == orgId && x.IsActive && requestedTeamIds.Contains(x.TeamId)).Select(x => x.TeamId).ToListAsync(ct);
        if (validTeamIds.Count != requestedTeamIds.Count) throw new InvalidOperationException("包含不存在或不屬於本 Organization 的小組。");

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var target = await db.Users.FirstOrDefaultAsync(x => x.UserId == userId && x.OrganizationId == orgId, ct)
                ?? throw new KeyNotFoundException("找不到人員。");

            var existingScopes = await db.UserTeamScopes.Where(x => x.UserId == userId).ToListAsync(ct);

            // SQL Server filtered unique index allows only one active primary team.
            // Clear the current primary first inside the same transaction so switching
            // from one existing scope to another cannot depend on UPDATE ordering.
            var currentPrimaryScopes = existingScopes.Where(x => x.IsActive && x.IsPrimary).ToList();
            if (currentPrimaryScopes.Count > 0)
            {
                foreach (var scope in currentPrimaryScopes) scope.IsPrimary = false;
                await db.SaveChangesAsync(ct);
            }

            var roleRows = await db.Roles.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
            var targetRoleIds = roleRows.Where(x => requestedRoles.Contains(NormalizeRole(x.RoleCode))).Select(x => x.RoleId).ToHashSet();
            var existingRoles = await db.UserRoles.Where(x => x.UserId == userId).ToListAsync(ct);
            db.UserRoles.RemoveRange(existingRoles.Where(x => !targetRoleIds.Contains(x.RoleId)));
            foreach (var roleId in targetRoleIds.Where(id => existingRoles.All(x => x.RoleId != id)))
                await db.UserRoles.AddAsync(new UserRole { UserId = userId, RoleId = roleId, AssignedAt = DateTime.UtcNow }, ct);

            foreach (var scope in existingScopes)
            {
                var requested = request.TeamScopes.FirstOrDefault(x => x.TeamId == scope.TeamId);
                scope.IsActive = requested is not null;
                scope.IsPrimary = requested?.IsPrimary == true;
                scope.EndedAt = requested is null ? DateTime.UtcNow : null;
                if (requested is not null) { scope.AssignedAt = DateTime.UtcNow; scope.AssignedByUserId = user.UserId; }
            }

            foreach (var requested in request.TeamScopes.Where(x => existingScopes.All(e => e.TeamId != x.TeamId)))
            {
                await db.UserTeamScopes.AddAsync(new UserTeamScope
                {
                    UserId = userId, TeamId = requested.TeamId, IsPrimary = requested.IsPrimary, IsActive = true,
                    AssignedAt = DateTime.UtcNow, AssignedByUserId = user.UserId
                }, ct);
            }

            target.TeamId = request.TeamScopes.FirstOrDefault(x => x.IsPrimary)?.TeamId;
            target.IsActive = request.IsActive;
            target.UpdatedAt = DateTime.UtcNow;

            AddAudit(user.UserId, "User", userId.ToString(), "UserAccessUpdated",
                new { Roles = requestedRoles, TeamScopes = request.TeamScopes });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return (await GetUsersAsync(user, ct)).First(x => x.UserId == userId);
    }

    public async Task<IReadOnlyList<ManagedTeamDto>> GetManagedTeamsAsync(CurrentUserDto user, bool includeInactive, CancellationToken ct)
    {
        var orgId = RequireOrganization(user);
        var q = db.Teams.AsNoTracking().Where(x => x.OrganizationId == orgId);
        if (!includeInactive) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.TeamCode)
            .Select(x => new ManagedTeamDto(x.TeamId, x.OrganizationId, x.TeamCode, x.TeamName, x.IsActive))
            .ToListAsync(ct);
    }

    public async Task<ManagedTeamDto> CreateManagedTeamAsync(CurrentUserDto user, SaveManagedTeamRequest request, CancellationToken ct)
    {
        var orgId = RequireOrganization(user);
        var code = NormalizeTeamCode(request.TeamCode);
        var name = NormalizeTeamName(request.TeamName);
        if (await db.Teams.AnyAsync(x => x.OrganizationId == orgId && x.TeamCode == code, ct))
            throw new InvalidOperationException("小組代碼已存在。");
        var row = new Team
        {
            OrganizationId = orgId,
            TeamCode = code,
            TeamName = name,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        await db.Teams.AddAsync(row, ct);
        AddAudit(user.UserId, "Team", null, "TeamCreate", new { row.TeamCode, row.TeamName, row.IsActive });
        await db.SaveChangesAsync(ct);
        return new ManagedTeamDto(row.TeamId, row.OrganizationId, row.TeamCode, row.TeamName, row.IsActive);
    }

    public async Task<ManagedTeamDto> UpdateManagedTeamAsync(CurrentUserDto user, int teamId, SaveManagedTeamRequest request, CancellationToken ct)
    {
        var orgId = RequireOrganization(user);
        var row = await db.Teams.FirstOrDefaultAsync(x => x.TeamId == teamId && x.OrganizationId == orgId, ct)
            ?? throw new KeyNotFoundException("找不到小組。");
        var code = NormalizeTeamCode(request.TeamCode);
        var name = NormalizeTeamName(request.TeamName);
        if (await db.Teams.AnyAsync(x => x.OrganizationId == orgId && x.TeamId != teamId && x.TeamCode == code, ct))
            throw new InvalidOperationException("小組代碼已存在。");
        if (row.IsActive && !request.IsActive) await EnsureTeamCanDeactivateAsync(teamId, ct);
        var before = new { row.TeamCode, row.TeamName, row.IsActive };
        row.TeamCode = code;
        row.TeamName = name;
        row.IsActive = request.IsActive;
        row.UpdatedAt = DateTime.UtcNow;
        AddAudit(user.UserId, "Team", teamId.ToString(), "TeamUpdate", new { before, after = new { row.TeamCode, row.TeamName, row.IsActive } });
        await db.SaveChangesAsync(ct);
        return new ManagedTeamDto(row.TeamId, row.OrganizationId, row.TeamCode, row.TeamName, row.IsActive);
    }

    public async Task DeactivateManagedTeamAsync(CurrentUserDto user, int teamId, CancellationToken ct)
    {
        var orgId = RequireOrganization(user);
        var row = await db.Teams.FirstOrDefaultAsync(x => x.TeamId == teamId && x.OrganizationId == orgId, ct)
            ?? throw new KeyNotFoundException("找不到小組。");
        if (!row.IsActive) return;
        await EnsureTeamCanDeactivateAsync(teamId, ct);
        row.IsActive = false;
        row.UpdatedAt = DateTime.UtcNow;
        AddAudit(user.UserId, "Team", teamId.ToString(), "TeamDeactivate", new { row.TeamCode, row.TeamName });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ManagedLocationDto>> GetManagedLocationsAsync(CurrentUserDto user, bool includeInactive, CancellationToken ct)
    {
        var q = db.Locations.AsNoTracking().AsQueryable();
        q = ApplyLocationScope(q, user);
        if (!includeInactive) q = q.Where(x => x.IsActive);
        var rows = await q.OrderBy(x => x.City).ThenBy(x => x.District).ThenBy(x => x.LocationName).ToListAsync(ct);
        var ids = rows.Where(x => x.TeamId.HasValue).Select(x => x.TeamId!.Value).Distinct().ToList();
        var teams = await db.Teams.AsNoTracking().Where(x => ids.Contains(x.TeamId)).ToDictionaryAsync(x => x.TeamId, ct);
        return rows.Select(x => MapManagedLocation(x, x.TeamId.HasValue && teams.TryGetValue(x.TeamId.Value, out var t) ? t.TeamName : null)).ToList();
    }

    public async Task<ManagedLocationDto> CreateManagedLocationAsync(CurrentUserDto user, SaveManagedLocationRequest request, CancellationToken ct)
    {
        ValidateLocationRequest(user, request);
        await EnsureManagedLocationTeamAsync(user, request.TeamId, ct);
        var orgId = RequireOrganization(user);
        var row = new Location
        {
            OrganizationId = orgId, TeamId = request.TeamId, LocationCode = NewLocationCode(), LocationName = request.LocationName.Trim(),
            LocationType = string.IsNullOrWhiteSpace(request.LocationType) ? "Official" : request.LocationType.Trim(), City = request.City?.Trim(), District = request.District?.Trim(),
            Address = request.Address?.Trim(), PlusCode = request.PlusCode?.Trim(), IsTemporary = false, ApprovalStatus = "Pending",
            GeocodingStatus = "Pending", CreatedByUserId = user.UserId, IsActive = false, CreatedAt = DateTime.UtcNow
        };
        await db.Locations.AddAsync(row, ct);
        AddAudit(user.UserId, "Location", null, "LocationCreate", new { row.LocationCode, row.LocationName, row.TeamId });
        await db.SaveChangesAsync(ct);
        var teamName = row.TeamId.HasValue ? await db.Teams.AsNoTracking().Where(x => x.TeamId == row.TeamId).Select(x => x.TeamName).FirstOrDefaultAsync(ct) : null;
        return MapManagedLocation(row, teamName);
    }

    public async Task<ManagedLocationDto> UpdateManagedLocationAsync(CurrentUserDto user, int locationId, SaveManagedLocationRequest request, CancellationToken ct)
    {
        ValidateLocationRequest(user, request);
        await EnsureManagedLocationTeamAsync(user, request.TeamId, ct);
        var row = await db.Locations.FirstOrDefaultAsync(x => x.LocationId == locationId, ct) ?? throw new KeyNotFoundException("找不到地點。");
        EnsureLocationWriteScope(row, user);
        EnsureRowVersion(row.RowVersion, request.RowVersion);
        var before = new { row.LocationName, row.TeamId, row.City, row.District, row.Address, row.PlusCode, row.IsActive };
        row.TeamId = request.TeamId;
        row.LocationName = request.LocationName.Trim();
        row.LocationType = string.IsNullOrWhiteSpace(request.LocationType) ? row.LocationType : request.LocationType.Trim();
        row.City = request.City?.Trim(); row.District = request.District?.Trim(); row.Address = request.Address?.Trim(); row.PlusCode = request.PlusCode?.Trim();
        row.IsActive = request.IsActive && row.ApprovalStatus == "Approved";
        row.GeocodingStatus = "Pending";
        row.UpdatedAt = DateTime.UtcNow;
        AddAudit(user.UserId, "Location", locationId.ToString(), "LocationUpdate", new { before, after = request });
        await db.SaveChangesAsync(ct);
        var teamName = row.TeamId.HasValue ? await db.Teams.AsNoTracking().Where(x => x.TeamId == row.TeamId).Select(x => x.TeamName).FirstOrDefaultAsync(ct) : null;
        return MapManagedLocation(row, teamName);
    }

    public async Task DeactivateManagedLocationAsync(CurrentUserDto user, int locationId, CancellationToken ct)
    {
        var row = await db.Locations.FirstOrDefaultAsync(x => x.LocationId == locationId, ct) ?? throw new KeyNotFoundException("找不到地點。");
        EnsureLocationWriteScope(row, user);
        row.IsActive = false;
        row.UpdatedAt = DateTime.UtcNow;
        AddAudit(user.UserId, "Location", locationId.ToString(), "LocationDeactivate", new { locationId });
        await db.SaveChangesAsync(ct);
    }

    public async Task<DashboardSummaryDto> GetDashboardAsync(CurrentUserDto user, CancellationToken ct)
    {
        var today = BusinessTime.Today;
        var start = new DateOnly(today.Year, today.Month, 1);
        var trips = ApplyTripScope(db.VisitTrips.AsNoTracking(), user).Where(x => x.Status != TripStatuses.Cancelled && x.VisitDate >= start && x.VisitDate <= today);
        var thisMonth = await trips.CountAsync(ct);
        var pending = await trips.CountAsync(x => x.Status == TripStatuses.PendingApproval, ct);
        var approved = await trips.CountAsync(x => x.Status == TripStatuses.Approved, ct);
        var pendingLocations = await ApplyLocationScope(db.Locations.AsNoTracking(), user).CountAsync(x => x.ApprovalStatus == "Pending" || x.GeocodingStatus == "Pending", ct);
        var correctionQ = db.CorrectionRequests.AsNoTracking().Where(x => x.Status == "PendingLeaderReview" || x.Status == "PendingAdminClose");
        if ((HasRole(user, "admin") || HasRole(user, "supervisor")) && user.OrganizationId.HasValue)
            correctionQ = correctionQ.Where(x => db.VisitTrips.Any(t => t.VisitTripId == x.VisitTripId && t.OrganizationId == user.OrganizationId.Value));
        else if (HasRole(user, "leader")) correctionQ = correctionQ.Where(x => db.VisitTrips.Any(t => t.VisitTripId == x.VisitTripId && t.TeamId.HasValue && user.TeamIds.Contains(t.TeamId.Value)));
        else if (HasRole(user, "visitor")) correctionQ = correctionQ.Where(x => x.RequestedByUserId == user.UserId);
        var pendingCorrections = await correctionQ.CountAsync(ct);
        decimal? rate = null;
        if (user.OrganizationId.HasValue)
            rate = await db.MileageRateRules.AsNoTracking().Where(x => x.IsActive && (x.OrganizationId == user.OrganizationId || x.OrganizationId == null) && x.VehicleType == "Motorcycle" && x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today)).OrderByDescending(x => x.OrganizationId.HasValue).ThenByDescending(x => x.EffectiveFrom).Select(x => (decimal?)x.RatePerKm).FirstOrDefaultAsync(ct);
        return new DashboardSummaryDto(thisMonth, pending, approved, pendingLocations, pendingCorrections, rate);
    }

    public async Task AuditExportAsync(CurrentUserDto user, string format, TripQueryRequest request, int count, CancellationToken ct)
    {
        AddAudit(user.UserId, "Report", null, "ReportExport", new { Format = format, Filters = request, Count = count });
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<VisitTrip> ApplyTripScope(IQueryable<VisitTrip> q, CurrentUserDto user)
    {
        if ((HasRole(user, "admin") || HasRole(user, "supervisor")) && user.OrganizationId.HasValue)
            return q.Where(x => x.OrganizationId == user.OrganizationId.Value);
        if (HasRole(user, "leader"))
        {
            var teamIds = user.TeamIds;
            return teamIds.Count == 0 ? q.Where(x => false) : q.Where(x => x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value));
        }
        if (HasRole(user, "visitor")) return q.Where(x => x.UserId == user.UserId);
        return q.Where(x => false);
    }

    private IQueryable<Location> ApplyLocationScope(IQueryable<Location> q, CurrentUserDto user)
    {
        if (user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value || x.OrganizationId == null);
        if (HasRole(user, "admin") || HasRole(user, "supervisor")) return q;
        if (HasRole(user, "leader"))
        {
            var teamIds = user.TeamIds;
            q = teamIds.Count == 0 ? q.Where(x => false) : q.Where(x => x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value));
        }
        else if (HasRole(user, "visitor")) q = q.Where(x => x.TeamId == user.TeamId || x.TeamId == null);
        return q;
    }

    private async Task EnsureTeamCanDeactivateAsync(int teamId, CancellationToken ct)
    {
        var hasScopes = await db.UserTeamScopes.AnyAsync(x => x.TeamId == teamId, ct);
        var hasPrimaryUsers = await db.Users.AnyAsync(x => x.TeamId == teamId, ct);
        if (hasScopes || hasPrimaryUsers)
            throw new InvalidOperationException("小組仍有成員或主要小組關聯，請先在小組成員維護移除或轉移後再停用。");
    }

    private static string NormalizeTeamCode(string value)
    {
        var code = (value ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("小組代碼必填。");
        if (code.Length > 50) throw new InvalidOperationException("小組代碼不可超過 50 個字元。");
        return code;
    }

    private static string NormalizeTeamName(string value)
    {
        var name = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("小組名稱必填。");
        if (name.Length > 100) throw new InvalidOperationException("小組名稱不可超過 100 個字元。");
        return name;
    }

    private async Task EnsureManagedLocationTeamAsync(CurrentUserDto user, int? teamId, CancellationToken ct)
    {
        if (!teamId.HasValue) return;
        var orgId = RequireOrganization(user);
        var valid = await db.Teams.AsNoTracking().AnyAsync(x => x.TeamId == teamId.Value && x.OrganizationId == orgId && x.IsActive, ct);
        if (!valid) throw new InvalidOperationException("所選小組不存在、已停用或不屬於目前 Organization。");
    }

    private void ValidateLocationRequest(CurrentUserDto user, SaveManagedLocationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LocationName)) throw new InvalidOperationException("地點名稱必填。");
        if (string.IsNullOrWhiteSpace(request.Address) && string.IsNullOrWhiteSpace(request.PlusCode)) throw new InvalidOperationException("地址與 Plus Code 至少需要一項。");
        if (HasRole(user, "leader") && (!request.TeamId.HasValue || !user.TeamIds.Contains(request.TeamId.Value))) throw new UnauthorizedAccessException("小組長只能維護授權小組地點。");
    }

    private void EnsureLocationWriteScope(Location row, CurrentUserDto user)
    {
        if (user.OrganizationId.HasValue && row.OrganizationId.HasValue && row.OrganizationId != user.OrganizationId) throw new UnauthorizedAccessException("無權維護其他 Organization 地點。");
        if (HasRole(user, "leader") && (!row.TeamId.HasValue || !user.TeamIds.Contains(row.TeamId.Value))) throw new UnauthorizedAccessException("無權維護未授權小組地點。");
    }

    private async Task<VisitTripSnapshot?> GetLatestSnapshotAsync(long tripId, CancellationToken ct) =>
        await db.VisitTripSnapshots.AsNoTracking().Include(x => x.Stops).Where(x => x.VisitTripId == tripId).OrderByDescending(x => x.SnapshotVersion).FirstOrDefaultAsync(ct);

    private static CorrectionProposal ProposalFrom(VisitTripSnapshot snapshot) => new(
        snapshot.VisitDate, snapshot.StartTime, snapshot.EndTime, snapshot.NotesSnapshot,
        snapshot.ClaimedDistanceKmSnapshot, snapshot.ApprovedDistanceKmSnapshot, snapshot.RatePerKmSnapshot,
        snapshot.SubsidyAmountSnapshot,
        snapshot.Stops.OrderBy(x => x.StopSequence).Select(x => new CorrectionStopProposal(
            x.StopSequence, x.LocationCodeSnapshot, x.LocationNameSnapshot, x.AddressSnapshot, x.ProjectCodeSnapshot,
            x.ProjectNameSnapshot, x.VisitTypeCodeSnapshot, x.VisitTypeNameSnapshot, x.VisitPurposeSnapshot, x.NotesSnapshot)).ToList());

    private static List<CorrectionChangeDto> Diff(VisitTripSnapshot old, CorrectionProposal proposed)
    {
        var changes = new List<CorrectionChangeDto>();
        AddDiff(changes, "VisitDate", old.VisitDate, proposed.VisitDate);
        AddDiff(changes, "StartTime", old.StartTime, proposed.StartTime);
        AddDiff(changes, "EndTime", old.EndTime, proposed.EndTime);
        AddDiff(changes, "Notes", old.NotesSnapshot, proposed.Notes);
        AddDiff(changes, "ClaimedDistanceKm", old.ClaimedDistanceKmSnapshot, proposed.ClaimedDistanceKm);
        AddDiff(changes, "ApprovedDistanceKm", old.ApprovedDistanceKmSnapshot, proposed.ApprovedDistanceKm);
        AddDiff(changes, "RatePerKm", old.RatePerKmSnapshot, proposed.RatePerKm);
        AddDiff(changes, "SubsidyAmount", old.SubsidyAmountSnapshot, proposed.SubsidyAmount);
        var oldStops = JsonSerializer.Serialize(ProposalFrom(old).Stops, JsonOptions);
        var newStops = JsonSerializer.Serialize(proposed.Stops, JsonOptions);
        if (!string.Equals(oldStops, newStops, StringComparison.Ordinal)) changes.Add(new CorrectionChangeDto("Stops", oldStops, newStops));
        return changes;
    }

    private static void AddDiff<T>(List<CorrectionChangeDto> list, string field, T oldValue, T newValue)
    {
        var a = oldValue is null ? null : oldValue.ToString(); var b = newValue is null ? null : newValue.ToString();
        if (!string.Equals(a, b, StringComparison.Ordinal)) list.Add(new CorrectionChangeDto(field, a, b));
    }

    private static bool RequiresAdminClose(IEnumerable<CorrectionRequestChange> changes) =>
        changes.Any(x => x.FieldName is "ApprovedDistanceKm" or "RatePerKm" or "SubsidyAmount");

    private async Task<VisitTripSnapshot> CreateCorrectionSnapshotAsync(CorrectionRequest row, int actorUserId, CancellationToken ct)
    {
        var baseSnapshot = await db.VisitTripSnapshots.AsNoTracking().Include(x => x.Stops).FirstAsync(x => x.VisitTripSnapshotId == row.BaseSnapshotId, ct);
        var proposal = JsonSerializer.Deserialize<CorrectionProposal>(row.ProposedChangesJson ?? "{}", JsonOptions) ?? throw new InvalidOperationException("更正內容無法解析。");
        var maxVersion = await db.VisitTripSnapshots.Where(x => x.VisitTripId == row.VisitTripId).MaxAsync(x => (int?)x.SnapshotVersion, ct) ?? 0;
        var snapshot = new VisitTripSnapshot
        {
            VisitTripId = baseSnapshot.VisitTripId, SnapshotVersion = maxVersion + 1, SnapshotType = "Correction",
            TripNo = baseSnapshot.TripNo, UserId = baseSnapshot.UserId, EmployeeNoSnapshot = baseSnapshot.EmployeeNoSnapshot,
            DisplayNameSnapshot = baseSnapshot.DisplayNameSnapshot, OrganizationId = baseSnapshot.OrganizationId,
            OrganizationNameSnapshot = baseSnapshot.OrganizationNameSnapshot, TeamId = baseSnapshot.TeamId, TeamNameSnapshot = baseSnapshot.TeamNameSnapshot,
            VisitDate = proposal.VisitDate, StartTime = proposal.StartTime, EndTime = proposal.EndTime, StatusSnapshot = TripStatuses.Approved,
            VehicleTypeSnapshot = baseSnapshot.VehicleTypeSnapshot, ClaimedDistanceKmSnapshot = proposal.ClaimedDistanceKm,
            SystemDistanceKmSnapshot = baseSnapshot.SystemDistanceKmSnapshot, ApprovedDistanceKmSnapshot = proposal.ApprovedDistanceKm,
            RatePerKmSnapshot = proposal.RatePerKm, SubsidyAmountSnapshot = proposal.SubsidyAmount,
            RouteProviderSnapshot = baseSnapshot.RouteProviderSnapshot, SubmittedAtSnapshot = baseSnapshot.SubmittedAtSnapshot,
            ApprovedAtSnapshot = baseSnapshot.ApprovedAtSnapshot, ApproverUserId = baseSnapshot.ApproverUserId,
            ApproverNameSnapshot = baseSnapshot.ApproverNameSnapshot, NotesSnapshot = proposal.Notes,
            CreatedAt = DateTime.UtcNow, CreatedByUserId = actorUserId
        };
        var baseStops = baseSnapshot.Stops.ToDictionary(x => x.StopSequence);
        foreach (var p in proposal.Stops.OrderBy(x => x.StopSequence))
        {
            baseStops.TryGetValue(p.StopSequence, out var old);
            snapshot.Stops.Add(new VisitTripSnapshotStop
            {
                StopSequence = p.StopSequence, LocationId = old?.LocationId, LocationCodeSnapshot = p.LocationCode,
                LocationNameSnapshot = p.LocationName, AddressSnapshot = p.Address, ProjectId = old?.ProjectId,
                ProjectCodeSnapshot = p.ProjectCode, ProjectNameSnapshot = p.ProjectName, VisitTypeId = old?.VisitTypeId,
                VisitTypeCodeSnapshot = p.VisitTypeCode, VisitTypeNameSnapshot = p.VisitTypeName, VisitPurposeSnapshot = p.VisitPurpose,
                NotesSnapshot = p.Notes, CreatedAt = DateTime.UtcNow
            });
        }
        await db.VisitTripSnapshots.AddAsync(snapshot, ct);
        await db.SaveChangesAsync(ct);
        return snapshot;
    }

    private async Task<CorrectionRequestDto> MapCorrectionAsync(long id, CancellationToken ct)
    {
        var row = await db.CorrectionRequests.AsNoTracking().FirstAsync(x => x.CorrectionRequestId == id, ct);
        var trip = await db.VisitTrips.AsNoTracking().FirstAsync(x => x.VisitTripId == row.VisitTripId, ct);
        var baseSnapshot = await db.VisitTripSnapshots.AsNoTracking().FirstAsync(x => x.VisitTripSnapshotId == row.BaseSnapshotId, ct);
        VisitTripSnapshot? resultSnapshot = null;
        if (row.ResultSnapshotId.HasValue) resultSnapshot = await db.VisitTripSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.VisitTripSnapshotId == row.ResultSnapshotId.Value, ct);
        var userIds = new[] { row.RequestedByUserId, row.LeaderReviewedByUserId ?? 0, row.AdminClosedByUserId ?? 0 }.Where(x => x > 0).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);
        var changes = await db.CorrectionRequestChanges.AsNoTracking().Where(x => x.CorrectionRequestId == id).OrderBy(x => x.CorrectionRequestChangeId).Select(x => new CorrectionChangeDto(x.FieldName, x.OldValue, x.NewValue)).ToListAsync(ct);
        var proposal = JsonSerializer.Deserialize<CorrectionProposal>(row.ProposedChangesJson ?? "{}", JsonOptions) ?? ProposalFrom(await GetLatestSnapshotAsync(row.VisitTripId, ct) ?? throw new InvalidOperationException("找不到 Snapshot。"));
        return new CorrectionRequestDto(
            row.CorrectionRequestId, row.VisitTripId, baseSnapshot.TripNo, baseSnapshot.DisplayNameSnapshot, baseSnapshot.TeamNameSnapshot,
            baseSnapshot.SnapshotVersion, resultSnapshot?.SnapshotVersion, row.Status, row.Reason, row.RequestedAt,
            names.GetValueOrDefault(row.RequestedByUserId, $"User {row.RequestedByUserId}"), row.LeaderReviewedAt,
            row.LeaderReviewedByUserId.HasValue ? names.GetValueOrDefault(row.LeaderReviewedByUserId.Value) : null, row.LeaderComments,
            row.AdminClosedAt, row.AdminClosedByUserId.HasValue ? names.GetValueOrDefault(row.AdminClosedByUserId.Value) : null,
            row.AdminComments, RequiresAdminClose(await db.CorrectionRequestChanges.AsNoTracking().Where(x => x.CorrectionRequestId == id).ToListAsync(ct)),
            proposal, changes, Convert.ToBase64String(row.RowVersion ?? []));
    }

    private static string JoinDistinct(IEnumerable<string?> values) => string.Join("、", values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct());
    private static bool HasRole(CurrentUserDto user, string role) => user.Roles.Any(x => x.Equals(role, StringComparison.OrdinalIgnoreCase));
    private static string NormalizeRole(string role) => role.Trim().ToLowerInvariant() switch { "government" => "supervisor", var x => x };
    private static int RequireOrganization(CurrentUserDto user) => user.OrganizationId ?? throw new InvalidOperationException("目前帳號缺少 OrganizationId。");
    private static string NewLocationCode() => $"LOC-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private static ManagedLocationDto MapManagedLocation(Location x, string? teamName) => new(
        x.LocationId, x.LocationCode ?? "", x.TeamId, teamName, x.LocationName, x.LocationType, x.City, x.District, x.Address, x.PlusCode,
        x.Latitude, x.Longitude, x.IsTemporary, x.ApprovalStatus, x.GeocodingStatus, x.IsActive, x.CreatedAt, Convert.ToBase64String(x.RowVersion ?? []));

    private static void EnsureRowVersion(byte[] currentValue, string? expectedBase64)
    {
        if (string.IsNullOrWhiteSpace(expectedBase64)) return;
        byte[] expected;
        try { expected = Convert.FromBase64String(expectedBase64); }
        catch { throw new InvalidOperationException("RowVersion 格式不正確。"); }
        if (!currentValue.SequenceEqual(expected)) throw new InvalidOperationException("ROWVERSION_CONFLICT：資料已被其他使用者修改，請重新整理。");
    }

    private void AddAudit(int? userId, string entityType, string? entityId, string action, object value) => db.AuditLogs.Add(new AuditLog
    {
        UserId = userId, EntityType = entityType, EntityId = entityId, Action = action,
        NewValues = JsonSerializer.Serialize(value, JsonOptions), CorrelationId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow
    });
}
