using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed partial class V160FinalRepository(AppDbContext db, IV170AccessControl access) : IV160FinalRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult<TripQueryRowDto>> QueryTripsAsync(CurrentUserDto user, TripQueryRequest request, bool exportAll, CancellationToken ct)
    {
        var latestSnapshotQ = db.VisitTripSnapshots.AsNoTracking().Where(s =>
            !db.VisitTripSnapshots.Any(newer => newer.VisitTripId == s.VisitTripId && newer.SnapshotVersion > s.SnapshotVersion));

        IQueryable<VisitTrip> q =
            await ApplyTripScopeAsync(
                db.VisitTrips.AsNoTracking(),
                user,
                ct);
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
            var survivorMatches=db.Locations.AsNoTracking().Where(x=>
                x.LocationName.Contains(keyword)
                ||(x.Address!=null&&x.Address.Contains(keyword))
                ||(x.LocationCode!=null&&x.LocationCode.Contains(keyword))
                ||(x.PlusCode!=null&&x.PlusCode.Contains(keyword))
                ||(x.TaxId!=null&&x.TaxId.Contains(keyword)));
            var historicalAliasIds=db.Locations.AsNoTracking()
                .Where(x=>x.DuplicateOfLocationId.HasValue
                    &&survivorMatches.Any(s=>s.LocationId==x.DuplicateOfLocationId.Value))
                .Select(x=>x.LocationId);

            q = q.Where(t => t.Status == TripStatuses.Approved
                ? latestSnapshotQ.Any(s => s.VisitTripId == t.VisitTripId && s.Stops.Any(st =>
                    st.LocationNameSnapshot.Contains(keyword) ||
                    (st.AddressSnapshot != null && st.AddressSnapshot.Contains(keyword)) ||
                    (st.LocationCodeSnapshot != null && st.LocationCodeSnapshot.Contains(keyword)) ||
                    (st.LocationId.HasValue && historicalAliasIds.Contains(st.LocationId.Value))))
                : t.Stops.Any(st =>
                    (st.LocationNameSnapshot != null && st.LocationNameSnapshot.Contains(keyword)) ||
                    (st.AddressSnapshot != null && st.AddressSnapshot.Contains(keyword)) ||
                    (st.Location != null && st.Location.LocationCode != null && st.Location.LocationCode.Contains(keyword)) ||
                    (st.LocationId.HasValue && historicalAliasIds.Contains(st.LocationId.Value))));
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

        q = V180TripKeywordQuery.Apply(
            q, latestSnapshotQ, db.Users, db.Projects, db.VisitTypes,
            db.DeploymentSites, db.Centers, request.Keyword);

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
                    ResolveMileageSource(snapshot.RouteProviderSnapshot, snapshot.MileageRouteAttemptIdSnapshot, snapshot.SystemDistanceKmSnapshot),
                    stops.Count < V170TripMileageRules.MinimumVisitStopCount ? "NotApplicable" : snapshot.SystemDistanceKmSnapshot.HasValue ? "Calculated" : "Pending",
                    TripStatuses.Approved, TripStatuses.Display(TripStatuses.Approved), snapshot.SnapshotVersion, true,
                    snapshot.NotesSnapshot, null, correction, stops);
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
                    calc?.CalculationSource,
                    stops.Count < V170TripMileageRules.MinimumVisitStopCount ? "NotApplicable" : calc?.SystemDistanceKm.HasValue == true || string.Equals(calc?.CalculationSource, "ManualFallback", StringComparison.OrdinalIgnoreCase) ? "Calculated" : "Pending",
                    trip.Status, TripStatuses.Display(trip.Status), 0, false, trip.Notes, trip.ReturnReason, correction, stops);
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

        // Financial snapshot rule:
        // - Same VisitDate: preserve the frozen rate from the base approved Snapshot.
        // - Changed VisitDate: re-evaluate rate using the corrected business date.
        // Any valid route with at least one visit stop remains mileage/subsidy applicable.
        var correctedHasMileage = request.Proposal.Stops.Count >= V170TripMileageRules.MinimumVisitStopCount;
        decimal? correctedRate = null;
        if (correctedHasMileage)
        {
            if (request.Proposal.ClaimedDistanceKm is <= 0) throw new InvalidOperationException("人工備援里程如有填寫，必須大於 0。");
            if (request.Proposal.ApprovedDistanceKm is null or <= 0) throw new InvalidOperationException("有拜訪地點的更正必須保留大於 0 的核定里程。");

            if (V160CorrectionFinancialRules.ShouldPreserveSnapshotRate(
                    snapshot.VisitDate,
                    request.Proposal.VisitDate))
            {
                correctedRate = V160CorrectionFinancialRules.RequireSnapshotRate(
                    snapshot.RatePerKmSnapshot);
            }
            else
            {
                correctedRate = await db.MileageRateRules.AsNoTracking()
                    .Where(x => x.IsActive && (x.OrganizationId == trip.OrganizationId || x.OrganizationId == null) &&
                        x.VehicleType == (snapshot.VehicleTypeSnapshot ?? "Motorcycle") && x.EffectiveFrom <= request.Proposal.VisitDate &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo >= request.Proposal.VisitDate))
                    .OrderByDescending(x => x.OrganizationId.HasValue).ThenByDescending(x => x.EffectiveFrom)
                    .Select(x => (decimal?)x.RatePerKm).FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("找不到更正日期適用的補助費率。");
            }
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

    private async Task<IQueryable<CorrectionRequest>> ScopedCorrectionsAsync(CurrentUserDto user, CancellationToken ct)
    {
        var q = db.CorrectionRequests.AsNoTracking().AsQueryable();
        if (HasRole(user, "admin"))
        {
            var orgId =
                user.OrganizationId ?? -1;

            q = q.Where(
                x => db.VisitTrips.Any(
                    t =>
                        t.VisitTripId == x.VisitTripId
                        && t.OrganizationId == orgId));
        }
        else if (HasRole(user, "supervisor"))
        {
            var readScope =
                await access.ResolveReadScopeAsync(
                    user,
                    ct);

            var orgId =
                user.OrganizationId ?? -1;

            if (readScope.OrganizationWide)
            {
                q = q.Where(
                    x => db.VisitTrips.Any(
                        t =>
                            t.VisitTripId == x.VisitTripId
                            && t.OrganizationId == orgId));
            }
            else
            {
                var teamIds =
                    readScope.TeamIds;

                q = teamIds.Count == 0
                    ? q.Where(x => false)
                    : q.Where(
                        x => db.VisitTrips.Any(
                            t =>
                                t.VisitTripId == x.VisitTripId
                                && t.OrganizationId == orgId
                                && t.TeamId.HasValue
                                && teamIds.Contains(
                                    t.TeamId.Value)));
            }
        }
        else if (HasRole(user, "leader"))
        {
            var teamIds = user.TeamIds;
            q = q.Where(x => db.VisitTrips.Any(t => t.VisitTripId == x.VisitTripId && t.TeamId.HasValue && teamIds.Contains(t.TeamId.Value)));
        }
        else if (HasRole(user, "visitor")) q = q.Where(x => x.RequestedByUserId == user.UserId);
        else q = q.Where(x => false);
        return q;
    }

    public async Task<IReadOnlyList<CorrectionRequestDto>> GetCorrectionsAsync(CurrentUserDto user, string? status, CancellationToken ct)
    {
        var q = await ScopedCorrectionsAsync(user, ct);
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
                var snapshot = await CreateCorrectionSnapshotAsync(row, user, null, ct);
                row.ResultSnapshotId = snapshot.VisitTripSnapshotId;
                row.Status = "Closed";
            }
        }
        AddAudit(user.UserId, "CorrectionRequest", row.CorrectionRequestId.ToString(), request.Approve ? "CorrectionLeaderApproved" : "CorrectionLeaderRejected", new { request.Comments, row.Status });
        await db.SaveChangesAsync(ct);
        return await MapCorrectionAsync(row.CorrectionRequestId, ct);
    }

    public async Task<CorrectionRequestDto> CloseCorrectionAsync(
        CurrentUserDto user,
        long correctionRequestId,
        CloseCorrectionRequest request,
        CancellationToken ct)
    {
        var row = await db.CorrectionRequests.FirstOrDefaultAsync(
            x => x.CorrectionRequestId == correctionRequestId, ct)
            ?? throw new KeyNotFoundException("找不到更正申請。");
        if (row.Status != "PendingAdminClose")
            throw new InvalidOperationException("此更正申請目前不需要管理者結案。");
        EnsureRowVersion(row.RowVersion, request.RowVersion);

        var trip = await db.VisitTrips.AsNoTracking()
            .FirstAsync(x => x.VisitTripId == row.VisitTripId, ct);
        if (user.OrganizationId.HasValue && trip.OrganizationId != user.OrganizationId.Value)
            throw new UnauthorizedAccessException("無權處理其他 Organization 資料。");

        var transitionAt = DateTime.UtcNow;
        row.AdminClosedByUserId = user.UserId;
        row.AdminClosedAt = transitionAt;
        row.AdminComments = request.Comments;

        if (!request.Approve)
        {
            row.Status = "Rejected";
        }
        else
        {
            var snapshot = await CreateCorrectionSnapshotAsync(row, user, request, ct);
            row.ResultSnapshotId = snapshot.VisitTripSnapshotId;
            row.Status = "Closed";
        }

        AddAudit(
            user.UserId,
            "CorrectionRequest",
            row.CorrectionRequestId.ToString(),
            request.Approve ? "CorrectionAdminClosed" : "CorrectionAdminRejected",
            new { request.Comments, row.Status });
        await db.SaveChangesAsync(ct);
        return await MapCorrectionAsync(row.CorrectionRequestId, ct);
    }

    public async Task<IReadOnlyList<UserOptionDto>> GetScopedVisitorsAsync(CurrentUserDto user, CancellationToken ct)
    {
        var q = db.Users.AsNoTracking().Where(x => x.IsActive);

        // "query/visitors" is a Visitor selector, not a generic user selector.
        // v1.7 identity-enabled users use effective-dated UserRoleAssignments as
        // the role source of truth. Legacy users without an identity profile
        // keep the UserRoles compatibility fallback.
        var today = BusinessTime.Today;

        var currentVisitorUserIds =
            from assignment in db.UserRoleAssignments.AsNoTracking()
            join role in db.Roles.AsNoTracking()
                on assignment.RoleId equals role.RoleId
            where
                assignment.EffectiveFrom <= today
                && (!assignment.EffectiveTo.HasValue
                    || assignment.EffectiveTo >= today)
                && role.IsActive
                && role.RoleCode.ToUpper() == "VISITOR"
            select assignment.UserId;

        var legacyVisitorUserIds =
            from userRole in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking()
                on userRole.RoleId equals role.RoleId
            where
                role.IsActive
                && role.RoleCode.ToUpper() == "VISITOR"
                && !db.UserIdentityProfiles.Any(
                    profile => profile.UserId == userRole.UserId)
            select userRole.UserId;

        q = q.Where(
            x =>
                currentVisitorUserIds.Contains(x.UserId)
                || legacyVisitorUserIds.Contains(x.UserId));
        if (HasRole(user, "admin"))
        {
            if (!user.OrganizationId.HasValue)
                return [];

            q = q.Where(
                x => x.OrganizationId
                     == user.OrganizationId.Value);
        }
        else if (HasRole(user, "supervisor"))
        {
            if (!user.OrganizationId.HasValue)
                return [];

            var readScope =
                await access.ResolveReadScopeAsync(
                    user,
                    ct);

            var orgId = user.OrganizationId.Value;

            q = q.Where(
                x => x.OrganizationId == orgId);

            if (!readScope.OrganizationWide)
            {
                var allowedTeams =
                    readScope.TeamIds;

                q = allowedTeams.Count == 0
                    ? q.Where(x => false)
                    : q.Where(
                        x =>
                            db.UserTeamScopes.Any(
                                s =>
                                    s.UserId == x.UserId
                                    && s.IsActive
                                    && allowedTeams.Contains(
                                        s.TeamId)));
            }
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
        return rows.Select(x => new UserOptionDto(x.UserId, x.EmployeeNo ?? "", x.DisplayName, x.TeamId,
            x.TeamId.HasValue && teams.TryGetValue(x.TeamId.Value, out var t) ? t.TeamName : null)).ToList();
    }

    public async Task<IReadOnlyList<AdminUserAccessDto>> GetUsersAsync(CurrentUserDto user, CancellationToken ct)
    {
        var orgId = RequireOrganization(user);
        var users = await db.Users.AsNoTracking().Where(x => x.OrganizationId == orgId).OrderBy(x => x.EmployeeNo).ToListAsync(ct);
        return await MapUsersAsync(users, ct);
    }

    private async Task<IReadOnlyList<AdminUserAccessDto>> MapUsersAsync(List<User> users, CancellationToken ct)
    {
        var ids = users.Select(x => x.UserId).ToList();
        var roles = await (from ur in db.UserRoles.AsNoTracking() join r in db.Roles.AsNoTracking() on ur.RoleId equals r.RoleId where ids.Contains(ur.UserId) select new { ur.UserId, r.RoleCode }).ToListAsync(ct);
        var scopes = await (from s in db.UserTeamScopes.AsNoTracking() join t in db.Teams.AsNoTracking() on s.TeamId equals t.TeamId where ids.Contains(s.UserId) && s.IsActive select new { s.UserId, Dto = new TeamScopeDto(t.TeamId, t.TeamName, s.IsPrimary) }).ToListAsync(ct);
        return users.Select(x => new AdminUserAccessDto(
            x.UserId, x.EmployeeNo ?? "", x.DisplayName, x.Email, x.IsActive,
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

    public async Task<PagedResult<ManagedLocationDto>> SearchManagedLocationsAsync(
        CurrentUserDto user,
        ManagedLocationQueryRequest request,
        CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = request.PageSize is 20 or 50 or 100 ? request.PageSize : 50;
        var q = ApplyLocationScope(db.Locations.AsNoTracking(), user);

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var keyword = request.Q.Trim();
            q = q.Where(x =>
                x.LocationCode != null && x.LocationCode.Contains(keyword)
                || x.LocationName.Contains(keyword)
                || (x.Address != null && x.Address.Contains(keyword))
                || (x.PlusCode != null && x.PlusCode.Contains(keyword))
                || (x.TaxId != null && x.TaxId.Contains(keyword))
                || (x.MasterNote != null && x.MasterNote.Contains(keyword))
                || db.TeamLocationNotes.AsNoTracking().Any(n =>
                    n.LocationId == x.LocationId && n.Note != null && n.Note.Contains(keyword))
                || db.TeamLocationNoteHistories.AsNoTracking().Any(h =>
                    h.LocationId == x.LocationId
                    && ((h.NewNote != null && h.NewNote.Contains(keyword))
                        || (h.OldNote != null && h.OldNote.Contains(keyword)))));
        }

        if (request.TeamId.HasValue) q = q.Where(x => x.TeamId == request.TeamId.Value);
        if (!string.IsNullOrWhiteSpace(request.City))
        {
            var city = request.City.Trim();
            q = q.Where(x => x.City == city);
        }
        if (!string.IsNullOrWhiteSpace(request.District))
        {
            var district = request.District.Trim();
            q = q.Where(x => x.District == district);
        }
        if (!string.IsNullOrWhiteSpace(request.GeocodingStatus))
        {
            var status = request.GeocodingStatus.Trim();
            if (status.Equals("NeedsProcessing", StringComparison.OrdinalIgnoreCase))
                q = q.Where(x => x.ApprovalStatus == "Pending" || x.GeocodingStatus == "Pending" || x.GeocodingStatus == "Failed");
            else
                q = q.Where(x => x.GeocodingStatus == status);
        }
        if (request.IsActive.HasValue) q = q.Where(x => x.IsActive == request.IsActive.Value);

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderBy(x => x.City).ThenBy(x => x.District).ThenBy(x => x.LocationName).ThenBy(x => x.LocationId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var teamIds = rows.Where(x => x.TeamId.HasValue).Select(x => x.TeamId!.Value).Distinct().ToList();
        var teams = await db.Teams.AsNoTracking().Where(x => teamIds.Contains(x.TeamId)).ToDictionaryAsync(x => x.TeamId, ct);
        var items = rows.Select(x => MapManagedLocation(
            x,
            x.TeamId.HasValue && teams.TryGetValue(x.TeamId.Value, out var team) ? team.TeamName : null)).ToList();

        return new PagedResult<ManagedLocationDto>(items, page, pageSize, total);
    }

    public async Task<ManagedLocationDto> CreateManagedLocationAsync(CurrentUserDto user, SaveManagedLocationRequest request, CancellationToken ct)
    {
        ValidateLocationRequest(user, request);
        var effectiveTeams=HasRole(user,"admin")?user.TeamIds:await EffectiveLocationWriteTeamsAsync(user,ct);
        V180LocationOwnershipRules.EnsureDraftCreate(user,request.TeamId,effectiveTeams,request.IsActive,request.LocationType);
        await EnsureManagedLocationTeamAsync(user, request.TeamId, ct);
        var orgId = RequireOrganization(user);
        var row = new Location
        {
            OrganizationId = orgId, TeamId = request.TeamId, LocationCode = NewLocationCode(), LocationName = request.LocationName.Trim(),
            LocationType = string.IsNullOrWhiteSpace(request.LocationType) ? "Customer" : request.LocationType.Trim(), City = request.City?.Trim(), District = request.District?.Trim(),
            Address = request.Address?.Trim(), PlusCode = request.PlusCode?.Trim(), TaxId = string.IsNullOrWhiteSpace(request.TaxId) ? null : request.TaxId.Trim(), MasterNote = string.IsNullOrWhiteSpace(request.MasterNote) ? null : request.MasterNote.Trim(), IsTemporary = false, ApprovalStatus = "Pending",
            GeocodingStatus = "Pending", CreatedByUserId = user.UserId, IsActive = false, CreatedAt = DateTime.UtcNow
        };
        await db.Locations.AddAsync(row, ct);
        AddAudit(user.UserId, "Location", null, "LocationCreate", new { row.LocationCode, row.LocationName, row.TeamId });
        await db.SaveChangesAsync(ct);
        await V180LocationDuplicateGovernance.RefreshSuspectFlagAsync(db,row,user.UserId,ct);
        await db.SaveChangesAsync(ct);
        var teamName = row.TeamId.HasValue ? await db.Teams.AsNoTracking().Where(x => x.TeamId == row.TeamId).Select(x => x.TeamName).FirstOrDefaultAsync(ct) : null;
        return MapManagedLocation(row, teamName);
    }

    public async Task<ManagedLocationDto> UpdateManagedLocationAsync(CurrentUserDto user, int locationId, SaveManagedLocationRequest request, CancellationToken ct)
    {
        ValidateLocationRequest(user, request);
        var row = await db.Locations.FirstOrDefaultAsync(x => x.LocationId == locationId, ct) ?? throw new KeyNotFoundException("找不到地點。");
        var admin=HasRole(user,"admin");
        var effectiveTeams=admin?user.TeamIds:await EffectiveLocationWriteTeamsAsync(user,ct);
        V180LocationOwnershipRules.EnsureDraftUpdate(
            user,row.OrganizationId,row.TeamId,row.CreatedByUserId,
            row.ApprovalStatus,row.IsActive,effectiveTeams,request.TeamId,request.IsActive,
            request.LocationType,row.LocationType);
        await EnsureManagedLocationTeamAsync(user, request.TeamId, ct);
        EnsureLocationWriteScope(row, user);
        if(!admin&&string.IsNullOrWhiteSpace(request.RowVersion))
            throw new InvalidOperationException("ROWVERSION_REQUIRED：請重新載入後再修改地點。");
        EnsureRowVersion(row.RowVersion, request.RowVersion);
        var before = new { row.LocationName, row.TeamId, row.City, row.District, row.Address, row.PlusCode, row.TaxId, row.MasterNote, row.IsActive };
        var nextName=request.LocationName.Trim();
        var nextAddress=request.Address?.Trim();
        var nextPlus=request.PlusCode?.Trim();
        var nextTax=!admin&&request.TaxId is null?row.TaxId:
            string.IsNullOrWhiteSpace(request.TaxId)?null:request.TaxId.Trim();
        var nextNote=!admin&&request.MasterNote is null?row.MasterNote:
            string.IsNullOrWhiteSpace(request.MasterNote)?null:request.MasterNote.Trim();
        var needsGeocode=V180LocationMaterialChangeRules.RequiresGeocoding(
            row.Address,row.PlusCode,nextAddress,nextPlus);
        var needsDuplicate=V180LocationMaterialChangeRules.RequiresDuplicateRecheck(
            row.LocationName,row.Address,row.PlusCode,row.TaxId,
            nextName,nextAddress,nextPlus,nextTax);
        row.TeamId = request.TeamId;
        row.LocationName = nextName;
        row.LocationType = string.IsNullOrWhiteSpace(request.LocationType) ? row.LocationType : request.LocationType.Trim();
        row.City=request.City?.Trim(); row.District=request.District?.Trim();
        row.Address=nextAddress; row.PlusCode=nextPlus; row.TaxId=nextTax; row.MasterNote=nextNote;
        row.IsActive = request.IsActive && row.ApprovalStatus == "Approved";
        if(needsGeocode)row.GeocodingStatus="Pending";
        row.UpdatedAt = DateTime.UtcNow;
        AddAudit(user.UserId, "Location", locationId.ToString(), "LocationUpdate", new { before, after = request });
        if(needsDuplicate)
            await V180LocationDuplicateGovernance.RefreshSuspectFlagAsync(db,row,user.UserId,ct);
        await db.SaveChangesAsync(ct);
        var teamName = row.TeamId.HasValue ? await db.Teams.AsNoTracking().Where(x => x.TeamId == row.TeamId).Select(x => x.TeamName).FirstOrDefaultAsync(ct) : null;
        return MapManagedLocation(row, teamName);
    }

    public async Task DeactivateManagedLocationAsync(CurrentUserDto user, int locationId, CancellationToken ct)
    {
        await EnsureCurrentAdminManagedLocationAsync(user,ct);
        var row = await db.Locations.FirstOrDefaultAsync(x => x.LocationId == locationId, ct) ?? throw new KeyNotFoundException("找不到地點。");
        V180LocationAdminMutationRules.RequireScopedLocation(user,row.OrganizationId);
        EnsureLocationWriteScope(row, user);
        row.IsActive = false;
        row.UpdatedAt = DateTime.UtcNow;
        AddAudit(user.UserId, "Location", locationId.ToString(), "LocationDeactivate", new { locationId });
        await db.SaveChangesAsync(ct);
    }

    public async Task<ManagedLocationDeleteImpactDto> GetManagedLocationDeleteImpactAsync(
        CurrentUserDto user,
        int locationId,
        CancellationToken ct)
    {
        await EnsureCurrentAdminManagedLocationAsync(user,ct);
        var row = await db.Locations.AsNoTracking().FirstOrDefaultAsync(x => x.LocationId == locationId, ct)
            ?? throw new KeyNotFoundException("找不到地點。");
        V180LocationAdminMutationRules.RequireScopedLocation(user,row.OrganizationId);
        EnsureLocationWriteScope(row, user);

        var tripRefs = await db.VisitTripStops.AsNoTracking().CountAsync(x => x.LocationId == locationId, ct);
        var projectRefs = await db.ProjectLocations.AsNoTracking().CountAsync(x => x.LocationId == locationId, ct);
        var favoriteRefs = await db.UserFavoriteLocations.AsNoTracking().CountAsync(x => x.LocationId == locationId, ct);
        var approvalHistory = await db.LocationApprovalHistories.AsNoTracking().CountAsync(x => x.LocationId == locationId, ct);
        var governmentMatches = await db.GovernmentLocationMasters.AsNoTracking().CountAsync(x => x.MatchedLocationId == locationId, ct);
        // The original 5 counters are preserved for API backwards compatibility.
        // These additional references must also block permanent deletion, even
        // where the database relationship permits a nullable reference.
        var snapshotRefs = await db.VisitTripSnapshotStops.AsNoTracking().CountAsync(x => x.LocationId == locationId, ct);
        var notes = await db.TeamLocationNotes.AsNoTracking().CountAsync(x => x.LocationId == locationId, ct);
        var noteHistory = await db.TeamLocationNoteHistories.AsNoTracking().CountAsync(x => x.LocationId == locationId, ct);
        var geocodingHistory = await db.GeocodingAttempts.AsNoTracking().CountAsync(x => x.LocationId == locationId, ct);
        var deploymentHistory = await db.DeploymentSiteLocationAssignments.AsNoTracking().CountAsync(x => x.LocationId == locationId, ct);
        var duplicateReferences = await db.Locations.AsNoTracking().CountAsync(x => x.DuplicateOfLocationId == locationId, ct);
        var canDelete = V180ManagedLocationDeletionRules.CanPermanentlyDelete(
            tripRefs,projectRefs,favoriteRefs,approvalHistory,governmentMatches,
            snapshotRefs,notes,noteHistory,geocodingHistory,deploymentHistory,duplicateReferences);
        string? reason = null;
        if (!canDelete)
        {
            var reasons = new List<string>();
            if (tripRefs > 0) reasons.Add($"已有 {tripRefs} 筆行程引用");
            if (projectRefs > 0) reasons.Add($"已有 {projectRefs} 筆專案地點引用");
            if (favoriteRefs > 0) reasons.Add($"已有 {favoriteRefs} 筆常用地點引用");
            if (approvalHistory > 0) reasons.Add($"已有 {approvalHistory} 筆核准/解析歷史");
            if (governmentMatches > 0) reasons.Add($"已有 {governmentMatches} 筆政府主檔比對");
            if (snapshotRefs > 0) reasons.Add($"已有 {snapshotRefs} 筆行程 Snapshot 歷史");
            if (notes > 0) reasons.Add($"已有 {notes} 筆小組備註");
            if (noteHistory > 0) reasons.Add($"已有 {noteHistory} 筆備註變更歷史");
            if (geocodingHistory > 0) reasons.Add($"已有 {geocodingHistory} 筆地理解析歷史");
            if (deploymentHistory > 0) reasons.Add($"已有 {deploymentHistory} 筆派駐據點期間");
            if (duplicateReferences > 0) reasons.Add($"已有 {duplicateReferences} 筆合併/重複地點關聯");
            reason = string.Join("；", reasons) + "，因此只能停用，不能永久刪除。";
        }

        return new ManagedLocationDeleteImpactDto(
            row.LocationId, row.LocationCode ?? "", row.LocationName, canDelete,
            tripRefs, projectRefs, favoriteRefs, approvalHistory, governmentMatches, reason);
    }

    public async Task DeleteManagedLocationAsync(CurrentUserDto user, int locationId, CancellationToken ct)
    {
        await EnsureCurrentAdminManagedLocationAsync(user,ct);
        // Serialize dependency inspection and deletion to prevent references
        // arriving between the impact check and the destructive operation.
        await using var tx=await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,ct);
        var row = await db.Locations.FirstOrDefaultAsync(x => x.LocationId == locationId, ct)
            ?? throw new KeyNotFoundException("找不到地點。");
        V180LocationAdminMutationRules.RequireScopedLocation(user,row.OrganizationId);
        EnsureLocationWriteScope(row, user);

        var impact = await GetManagedLocationDeleteImpactAsync(user, locationId, ct);
        if (!impact.CanDelete)
            throw new InvalidOperationException(impact.Reason ?? "此地點已有歷史或關聯資料，只能停用。");

        var auditValue = new
        {
            row.LocationId, row.LocationCode, row.LocationName, row.TeamId,
            row.City, row.District, row.Address, row.PlusCode
        };
        db.Locations.Remove(row);
        AddAudit(user.UserId, "Location", locationId.ToString(), "LocationPermanentDelete", auditValue);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<DashboardSummaryDto> GetDashboardAsync(CurrentUserDto user, CancellationToken ct)
    {
        var today = BusinessTime.Today;
        var start = new DateOnly(today.Year, today.Month, 1);
        var trips =
            (await ApplyTripScopeAsync(
                db.VisitTrips.AsNoTracking(),
                user,
                ct))
            .Where(
                x =>
                    x.Status != TripStatuses.Cancelled
                    && x.VisitDate >= start
                    && x.VisitDate <= today);
        var thisMonth = await trips.CountAsync(ct);
        var pending = await trips.CountAsync(x => x.Status == TripStatuses.PendingApproval, ct);
        var approved = await trips.CountAsync(x => x.Status == TripStatuses.Approved, ct);
        var pendingLocations =
            HasRole(user, "supervisor")
                ? 0
                : await ApplyLocationScope(
                    db.Locations.AsNoTracking(),
                    user)
                    .CountAsync(
                        x =>
                            x.ApprovalStatus == "Pending"
                            || x.GeocodingStatus == "Pending"
                            || (x.DuplicateOfLocationId == null
                                && x.DuplicateReason == V170LocationDuplicateRules.SuspectedReason),
                        ct);
        var correctionQ = db.CorrectionRequests.AsNoTracking().Where(x => x.Status == "PendingLeaderReview" || x.Status == "PendingAdminClose");
        if (HasRole(user, "admin")
            && user.OrganizationId.HasValue)
        {
            correctionQ = correctionQ.Where(
                x => db.VisitTrips.Any(
                    t =>
                        t.VisitTripId == x.VisitTripId
                        && t.OrganizationId
                           == user.OrganizationId.Value));
        }
        else if (HasRole(user, "supervisor"))
        {
            // Supervisor has no correction workflow action.
            // Do not expose an irrelevant workflow count.
            correctionQ = correctionQ.Where(x => false);
        }
        else if (HasRole(user, "leader"))
        {
            correctionQ = correctionQ.Where(
                x => db.VisitTrips.Any(
                    t =>
                        t.VisitTripId == x.VisitTripId
                        && t.TeamId.HasValue
                        && user.TeamIds.Contains(
                            t.TeamId.Value)));
        }
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

    private async Task<IQueryable<VisitTrip>> ApplyTripScopeAsync(
        IQueryable<VisitTrip> q,
        CurrentUserDto user,
        CancellationToken ct)
    {
        if (HasRole(user, "admin")
            && user.OrganizationId.HasValue)
        {
            return q.Where(
                x => x.OrganizationId
                     == user.OrganizationId.Value);
        }

        if (HasRole(user, "supervisor"))
        {
            if (!user.OrganizationId.HasValue)
                return q.Where(x => false);

            var orgId =
                user.OrganizationId.Value;

            var readScope =
                await access.ResolveReadScopeAsync(
                    user,
                    ct);

            q = q.Where(
                x => x.OrganizationId == orgId);

            if (readScope.OrganizationWide)
                return q;

            var teamIds =
                readScope.TeamIds;

            return teamIds.Count == 0
                ? q.Where(x => false)
                : q.Where(
                    x =>
                        x.TeamId.HasValue
                        && teamIds.Contains(
                            x.TeamId.Value));
        }

        if (HasRole(user, "leader"))
        {
            var teamIds =
                user.TeamIds;

            return teamIds.Count == 0
                ? q.Where(x => false)
                : q.Where(
                    x =>
                        x.TeamId.HasValue
                        && teamIds.Contains(
                            x.TeamId.Value));
        }

        if (HasRole(user, "visitor"))
        {
            return q.Where(
                x => x.UserId == user.UserId);
        }

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
        else if (HasRole(user, "visitor")) q = q.Where(x => x.CreatedByUserId == user.UserId && x.TeamId.HasValue && user.TeamIds.Contains(x.TeamId.Value));
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

    /// <summary>
    /// Admin-only hard deletion / deactivation must use current HR and
    /// effective-dated Admin grants; JWT and UserRoles alone are insufficient.
    /// This is not a B3 approval or team-manager attestation.
    /// </summary>
    private async Task EnsureCurrentAdminManagedLocationAsync(CurrentUserDto user,CancellationToken ct)
    {
        if(!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("管理者缺少有效組織。");
        var account=await db.Users.AsNoTracking().FirstOrDefaultAsync(x=>
            x.UserId==user.UserId&&x.OrganizationId==user.OrganizationId,ct)
            ??throw new UnauthorizedAccessException("管理者帳號或組織授權已失效。");
        if(!(await access.EvaluateLoginAsync(user.UserId,account.IsActive,ct)).IsAllowed)
            throw new UnauthorizedAccessException("人事狀態無權停用或刪除地點。");
        var today=BusinessTime.Today;
        var dated=await (
            from grant in db.UserRoleAssignments.AsNoTracking()
            join role in db.Roles.AsNoTracking() on grant.RoleId equals role.RoleId
            where grant.UserId==user.UserId&&role.IsActive
                &&grant.EffectiveFrom<=today
                &&(!grant.EffectiveTo.HasValue||grant.EffectiveTo>=today)
            select role.RoleCode).ToListAsync(ct);
        var projected=await (
            from grant in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on grant.RoleId equals role.RoleId
            where grant.UserId==user.UserId&&role.IsActive
            select role.RoleCode).ToListAsync(ct);
        V180LocationAdminMutationRules.RequireCurrentAdmin(user,dated,projected);
    }

    private async Task<IReadOnlyList<int>> EffectiveLocationWriteTeamsAsync(CurrentUserDto user,CancellationToken ct)
    {
        if(!user.OrganizationId.HasValue||user.TeamIds.Count==0)
            throw new UnauthorizedAccessException("缺少目前有效授權的小組。");
        var account=await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x=>x.UserId==user.UserId&&x.OrganizationId==user.OrganizationId,ct)
            ??throw new UnauthorizedAccessException("帳號或組織權限已失效。");
        if(!(await access.EvaluateLoginAsync(user.UserId,account.IsActive,ct)).IsAllowed)
            throw new UnauthorizedAccessException("目前人事狀態不允許維護地點。");
        var today=BusinessTime.Today;
        var datedRoles=await (
            from grant in db.UserRoleAssignments.AsNoTracking()
            join role in db.Roles.AsNoTracking() on grant.RoleId equals role.RoleId
            where grant.UserId==user.UserId && role.IsActive
                && grant.EffectiveFrom<=today
                && (!grant.EffectiveTo.HasValue || grant.EffectiveTo.Value>=today)
            select role.RoleCode).ToListAsync(ct);
        var projectedRoles=await (
            from grant in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on grant.RoleId equals role.RoleId
            where grant.UserId==user.UserId && role.IsActive
            select role.RoleCode).ToListAsync(ct);
        var effectiveRoles=V180LocationLiveRoleRules.Evaluate(user.Roles,datedRoles,projectedRoles);
        // A leader role plus membership is NOT an attested team management grant.
        // Until Owner/IT reconciliation, only an independently effective Visitor
        // role may write that actor's own Pending Customer draft.
        if(!effectiveRoles.Visitor)
        {
            if(effectiveRoles.Leader)
                V180B1ManagerGrantProvenance.RequireVerifiedManagerGrant();
            throw new UnauthorizedAccessException("沒有有效外訪員角色或已核定的小組管理權限。");
        }
        var employmentId=await db.UserIdentityProfiles.AsNoTracking()
            .Where(x=>x.UserId==user.UserId && x.UserType==UserTypes.Internal)
            .Select(x=>x.EmploymentId).FirstOrDefaultAsync(ct);
        if(!employmentId.HasValue)
            throw new UnauthorizedAccessException("沒有已綁定的人事任用資料，無法確認小組授權。");
        var ids=user.TeamIds.ToArray();
        var teams=await (
            from scope in db.UserTeamScopes.AsNoTracking()
            join team in db.Teams.AsNoTracking() on scope.TeamId equals team.TeamId
            join userAssignment in db.UserTeamAssignments.AsNoTracking()
                on new {scope.UserId,scope.TeamId} equals new {userAssignment.UserId,userAssignment.TeamId}
            join membership in db.TeamMemberships.AsNoTracking()
                on new {EmploymentId=employmentId.Value,scope.TeamId}
                equals new {membership.EmploymentId,membership.TeamId}
            where scope.UserId==user.UserId && scope.IsActive
                && ids.Contains(scope.TeamId)
                && team.IsActive && team.OrganizationId==user.OrganizationId
                && (!team.EffectiveFrom.HasValue || team.EffectiveFrom<=today)
                && (!team.EffectiveTo.HasValue || team.EffectiveTo>=today)
                && userAssignment.EffectiveFrom<=today
                && (!userAssignment.EffectiveTo.HasValue || userAssignment.EffectiveTo.Value>=today)
                && membership.EffectiveFrom<=today
                && (!membership.EffectiveTo.HasValue || membership.EffectiveTo.Value>=today)
            select team.TeamId).Distinct().ToListAsync(ct);
        if(teams.Count==0)
            throw new UnauthorizedAccessException("沒有同時通過有效期間與組織檢核的小組維護權限。");
        return teams;
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
        if (request.TaxId?.Trim().Length > 20) throw new InvalidOperationException("統一編號長度不可超過 20 字元。");
        if (request.MasterNote?.Trim().Length > 1000) throw new InvalidOperationException("主檔備註不可超過 1000 字元。");
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

    private static string? ResolveMileageSource(
        string? routeProvider,
        long? routeAttemptId,
        decimal? systemDistanceKm)
    {
        if (string.Equals(routeProvider, "ManualFallback", StringComparison.OrdinalIgnoreCase))
            return "ManualFallback";
        if (string.Equals(routeProvider, "GoogleMapsRoutes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(routeProvider, "GoogleMapsAPI", StringComparison.OrdinalIgnoreCase))
            return "GoogleMapsAPI";
        if (!string.IsNullOrWhiteSpace(routeProvider))
            return routeProvider;
        return routeAttemptId.HasValue || systemDistanceKm is > 0
            ? "GoogleMapsAPI"
            : null;
    }

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
        if (V160CorrectionDiffRules.AreEquivalent(oldValue, newValue))
            return;

        list.Add(
            new CorrectionChangeDto(
                field,
                V160CorrectionDiffRules.ToDisplayValue(oldValue),
                V160CorrectionDiffRules.ToDisplayValue(newValue)));
    }

    private static bool RequiresAdminClose(IEnumerable<CorrectionRequestChange> changes) =>
        changes.Any(x => x.FieldName is "ApprovedDistanceKm" or "RatePerKm" or "SubsidyAmount");

    private sealed record CorrectionSnapshotShadow(
        long? PersonId,
        long? EmploymentId,
        int? CenterId,
        string? CenterCode,
        string? CenterName,
        string? TeamCode,
        int? StartDeploymentSiteId,
        string? StartDeploymentSiteName,
        int? StartDeploymentLocationId,
        int? EndDeploymentSiteId,
        string? EndDeploymentSiteName,
        int? EndDeploymentLocationId);

    private async Task<VisitTripSnapshot> CreateCorrectionSnapshotAsync(
        CorrectionRequest row,
        CurrentUserDto actor,
        CloseCorrectionRequest? decisionRequest,
        CancellationToken ct)
    {
        var baseSnapshot = await db.VisitTripSnapshots.AsNoTracking()
            .Include(x => x.Stops)
            .FirstAsync(x => x.VisitTripSnapshotId == row.BaseSnapshotId, ct);
        var proposal = JsonSerializer.Deserialize<CorrectionProposal>(
            row.ProposedChangesJson ?? "{}", JsonOptions)
            ?? throw new InvalidOperationException("更正內容無法解析。");
        var shadow = await LoadCorrectionShadowAsync(baseSnapshot.VisitTripSnapshotId, ct);

        var distanceChanged = proposal.ApprovedDistanceKm != baseSnapshot.ApprovedDistanceKmSnapshot;
        var decisionSource = baseSnapshot.ApprovedDistanceSourceSnapshot;
        var approvalBasisCode = baseSnapshot.ApprovalBasisCodeSnapshot;
        var approvalBasisHash = baseSnapshot.ApprovalBasisHashSnapshot?.ToArray();
        var distanceApprovedAt = baseSnapshot.DistanceApprovedAtSnapshot;
        var approverUserId = baseSnapshot.ApproverUserId;
        var approverName = baseSnapshot.ApproverNameSnapshot;
        var routeAttemptId = baseSnapshot.MileageRouteAttemptIdSnapshot;
        var routeTravelMode = baseSnapshot.RouteTravelModeSnapshot;
        var routeCalculatedAt = baseSnapshot.RouteCalculatedAtSnapshot;
        var routeStatus = baseSnapshot.RouteCalculationStatusSnapshot;
        var routeErrorCode = baseSnapshot.RouteErrorCodeSnapshot;
        var routeCorrelationId = baseSnapshot.RouteCorrelationIdSnapshot;
        var routeProvider = baseSnapshot.RouteProviderSnapshot;

        RouteCalculationAttempt? selectedAttempt = null;
        if (distanceChanged)
        {
            if (!row.AdminClosedByUserId.HasValue || !row.AdminClosedAt.HasValue)
                throw new InvalidOperationException("F_B_CORRECTION_ADMIN_EVIDENCE_REQUIRED：距離更正必須先建立 Admin close evidence。");

            var requiredDecisionRequest = decisionRequest
                ?? throw new InvalidOperationException(
                    "F_B_CORRECTION_DECISION_REQUEST_REQUIRED：距離更正必須提供 Admin close decision request。");

            approvalBasisCode = V180MileageGovernanceRules.CorrectionProposalBasisCode;
            approvalBasisHash = V180MileageCanonicalization.HashCorrectionProposal(baseSnapshot, proposal);
            distanceApprovedAt = row.AdminClosedAt;
            approverUserId = row.AdminClosedByUserId;
            approverName = actor.DisplayName;

            var canonicalVehicle = V180MileageCanonicalization.CanonicalVehicleType(
                baseSnapshot.VehicleTypeSnapshot ?? "Motorcycle");
            var expectedVehicle = V180MileageCanonicalization.ToDbRequestedVehicleType(canonicalVehicle);
            var expectedTravelMode = V180MileageCanonicalization.ToTravelMode(canonicalVehicle);

            decisionSource = V180MileageGovernanceRules.ResolveCorrectionDecisionSource(
                requiredDecisionRequest.DistanceDecisionSource,
                requiredDecisionRequest.RouteCalculationAttemptId.HasValue);

            if (!requiredDecisionRequest.RouteCalculationAttemptId.HasValue)
            {
                routeAttemptId = null;
                routeTravelMode = expectedTravelMode;
                routeCalculatedAt = null;
                routeStatus = "ManualFallback";
                routeErrorCode = null;
                routeCorrelationId = Guid.NewGuid();
                routeProvider = "ManualFallback";
            }
            else
            {
                selectedAttempt = await db.RouteCalculationAttempts.AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.RouteCalculationAttemptId == requiredDecisionRequest.RouteCalculationAttemptId!.Value,
                        ct)
                    ?? throw new InvalidOperationException(
                        "F_B_CORRECTION_ROUTE_ATTEMPT_NOT_FOUND：找不到指定的 route attempt。");

                if (selectedAttempt.VisitTripId != row.VisitTripId
                    || selectedAttempt.CalculationReason != "CorrectionRecalculate"
                    || selectedAttempt.Status != "Succeeded"
                    || !selectedAttempt.RequestBasisHash.SequenceEqual(approvalBasisHash)
                    || selectedAttempt.RequestedVehicleType != expectedVehicle
                    || selectedAttempt.TravelMode != expectedTravelMode)
                    throw new InvalidOperationException(
                        "F_B_CORRECTION_ROUTE_ATTEMPT_STALE：既有 CorrectionRecalculate attempt 與 final CorrectionProposal basis 不一致。");

                routeAttemptId = selectedAttempt.RouteCalculationAttemptId;
                routeTravelMode = selectedAttempt.TravelMode;
                routeCalculatedAt = selectedAttempt.CompletedAt;
                routeStatus = selectedAttempt.Status;
                routeErrorCode = selectedAttempt.ErrorCode;
                routeCorrelationId = selectedAttempt.CorrelationId;
                routeProvider = selectedAttempt.Provider;
            }
        }

        var maxVersion = await db.VisitTripSnapshots
            .Where(x => x.VisitTripId == row.VisitTripId)
            .MaxAsync(x => (int?)x.SnapshotVersion, ct) ?? 0;

        var snapshot = new VisitTripSnapshot
        {
            VisitTripId = baseSnapshot.VisitTripId,
            SnapshotVersion = maxVersion + 1,
            SnapshotType = "Correction",
            TripNo = baseSnapshot.TripNo,
            UserId = baseSnapshot.UserId,
            EmployeeNoSnapshot = baseSnapshot.EmployeeNoSnapshot,
            DisplayNameSnapshot = baseSnapshot.DisplayNameSnapshot,
            OrganizationId = baseSnapshot.OrganizationId,
            OrganizationNameSnapshot = baseSnapshot.OrganizationNameSnapshot,
            TeamId = baseSnapshot.TeamId,
            TeamNameSnapshot = baseSnapshot.TeamNameSnapshot,
            StartDeploymentSiteCodeSnapshot = baseSnapshot.StartDeploymentSiteCodeSnapshot,
            StartDeploymentAddressSnapshot = baseSnapshot.StartDeploymentAddressSnapshot,
            EndDeploymentSiteCodeSnapshot = baseSnapshot.EndDeploymentSiteCodeSnapshot,
            EndDeploymentAddressSnapshot = baseSnapshot.EndDeploymentAddressSnapshot,
            VisitDate = proposal.VisitDate,
            StartTime = proposal.StartTime,
            EndTime = proposal.EndTime,
            StatusSnapshot = TripStatuses.Approved,
            VehicleTypeSnapshot = baseSnapshot.VehicleTypeSnapshot,
            ClaimedDistanceKmSnapshot = proposal.ClaimedDistanceKm,
            SystemDistanceKmSnapshot = baseSnapshot.SystemDistanceKmSnapshot,
            ApprovedDistanceKmSnapshot = proposal.ApprovedDistanceKm,
            RatePerKmSnapshot = proposal.RatePerKm,
            SubsidyAmountSnapshot = proposal.SubsidyAmount,
            RouteProviderSnapshot = routeProvider,
            SubmittedAtSnapshot = baseSnapshot.SubmittedAtSnapshot,
            ApprovedAtSnapshot = baseSnapshot.ApprovedAtSnapshot,
            ApproverUserId = approverUserId,
            ApproverNameSnapshot = approverName,
            NotesSnapshot = proposal.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = actor.UserId,
            MileageRouteAttemptIdSnapshot = routeAttemptId,
            RouteTravelModeSnapshot = routeTravelMode,
            RouteCalculatedAtSnapshot = routeCalculatedAt,
            RouteCalculationStatusSnapshot = routeStatus,
            RouteErrorCodeSnapshot = routeErrorCode,
            RouteCorrelationIdSnapshot = routeCorrelationId,
            ApprovedDistanceSourceSnapshot = decisionSource,
            ApprovalBasisCodeSnapshot = approvalBasisCode,
            ApprovalBasisHashSnapshot = approvalBasisHash,
            DistanceApprovedAtSnapshot = distanceApprovedAt
        };

        var baseStops = baseSnapshot.Stops.ToDictionary(x => x.StopSequence);
        foreach (var p in proposal.Stops.OrderBy(x => x.StopSequence))
        {
            baseStops.TryGetValue(p.StopSequence, out var old);
            snapshot.Stops.Add(new VisitTripSnapshotStop
            {
                StopSequence = p.StopSequence,
                LocationId = old?.LocationId,
                LocationCodeSnapshot = p.LocationCode,
                LocationNameSnapshot = p.LocationName,
                AddressSnapshot = p.Address,
                ProjectId = old?.ProjectId,
                ProjectCodeSnapshot = p.ProjectCode,
                ProjectNameSnapshot = p.ProjectName,
                VisitTypeId = old?.VisitTypeId,
                VisitTypeCodeSnapshot = p.VisitTypeCode,
                VisitTypeNameSnapshot = p.VisitTypeName,
                VisitPurposeSnapshot = p.VisitPurpose,
                NotesSnapshot = p.Notes,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.VisitTripSnapshots.AddAsync(snapshot, ct);
        SetCorrectionShadow(snapshot, shadow);

        if (distanceChanged)
        {
            db.MileageGovernanceEvents.Add(new MileageGovernanceEvent
            {
                VisitTripId = row.VisitTripId,
                VisitTripSnapshotId = null,
                RouteCalculationAttemptId = selectedAttempt?.RouteCalculationAttemptId,
                EventType = "Approved",
                ReasonCode = decisionSource,
                Message = "Correction distance decision finalized by Admin close.",
                CorrelationId = selectedAttempt?.CorrelationId ?? Guid.NewGuid(),
                OccurredAt = row.AdminClosedAt!.Value,
                ActorUserId = row.AdminClosedByUserId
            });
        }

        await db.SaveChangesAsync(ct);
        return snapshot;
    }

    private async Task<CorrectionSnapshotShadow> LoadCorrectionShadowAsync(
        long snapshotId,
        CancellationToken ct) =>
        await db.VisitTripSnapshots.AsNoTracking()
            .Where(x => x.VisitTripSnapshotId == snapshotId)
            .Select(x => new CorrectionSnapshotShadow(
                EF.Property<long?>(x, "PersonIdSnapshot"),
                EF.Property<long?>(x, "EmploymentIdSnapshot"),
                EF.Property<int?>(x, "CenterIdSnapshot"),
                EF.Property<string?>(x, "CenterCodeSnapshot"),
                EF.Property<string?>(x, "CenterNameSnapshot"),
                EF.Property<string?>(x, "TeamCodeSnapshot"),
                EF.Property<int?>(x, "StartDeploymentSiteIdSnapshot"),
                EF.Property<string?>(x, "StartDeploymentSiteNameSnapshot"),
                EF.Property<int?>(x, "StartDeploymentLocationIdSnapshot"),
                EF.Property<int?>(x, "EndDeploymentSiteIdSnapshot"),
                EF.Property<string?>(x, "EndDeploymentSiteNameSnapshot"),
                EF.Property<int?>(x, "EndDeploymentLocationIdSnapshot")))
            .SingleAsync(ct);

    private void SetCorrectionShadow(
        VisitTripSnapshot snapshot,
        CorrectionSnapshotShadow shadow)
    {
        var entry = db.Entry(snapshot);
        entry.Property<long?>("PersonIdSnapshot").CurrentValue = shadow.PersonId;
        entry.Property<long?>("EmploymentIdSnapshot").CurrentValue = shadow.EmploymentId;
        entry.Property<int?>("CenterIdSnapshot").CurrentValue = shadow.CenterId;
        entry.Property<string?>("CenterCodeSnapshot").CurrentValue = shadow.CenterCode;
        entry.Property<string?>("CenterNameSnapshot").CurrentValue = shadow.CenterName;
        entry.Property<string?>("TeamCodeSnapshot").CurrentValue = shadow.TeamCode;
        entry.Property<int?>("StartDeploymentSiteIdSnapshot").CurrentValue = shadow.StartDeploymentSiteId;
        entry.Property<string?>("StartDeploymentSiteNameSnapshot").CurrentValue = shadow.StartDeploymentSiteName;
        entry.Property<int?>("StartDeploymentLocationIdSnapshot").CurrentValue = shadow.StartDeploymentLocationId;
        entry.Property<int?>("EndDeploymentSiteIdSnapshot").CurrentValue = shadow.EndDeploymentSiteId;
        entry.Property<string?>("EndDeploymentSiteNameSnapshot").CurrentValue = shadow.EndDeploymentSiteName;
        entry.Property<int?>("EndDeploymentLocationIdSnapshot").CurrentValue = shadow.EndDeploymentLocationId;
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
        x.Latitude, x.Longitude, x.IsTemporary, x.ApprovalStatus, x.GeocodingStatus, x.IsActive, x.CreatedAt, Convert.ToBase64String(x.RowVersion ?? []),
        x.DuplicateOfLocationId, x.DuplicateReason, x.TaxId, x.MasterNote, x.CreatedByUserId);

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
