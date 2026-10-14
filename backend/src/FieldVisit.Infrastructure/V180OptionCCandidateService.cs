using System.Data;
using System.Text.Json;
using FieldVisit.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Non-deploy candidate composition for the Option C HTTP surface. It is
/// enabled only by an explicit candidate flag and an isolated loopback SQL
/// target; deployed Option C/B3 flags remain independent and false.
/// </summary>
public sealed record V180OptionCCreateLocation(
    int TeamId, string Reason, V180B3LocationFields Proposed);

/// <summary>Row-bound, independently reviewed Excel Create submission summary.</summary>
public sealed record V180OptionCExcelCreateResult(
    Guid ImportBatchId, int Submitted, int AlreadySubmitted, string Status,
    IReadOnlyList<Guid> RequestPublicIds);

internal sealed record V180OptionCExcelRowSource(Guid ImportBatchId, long ImportBatchItemId);
internal sealed record V180OptionCExcelLocationRow(
    string? LocationCode,string? TeamCode,string LocationName,
    string? City,string? District,string? Address,string? PlusCode,string Status,
    string? ExpectedEntityRowVersion = null);

public sealed class V180OptionCCandidateService(
    AppDbContext db, ICurrentUserService current, IConfiguration config)
{
    private bool Enabled => config.GetValue<bool>("OptionC:Candidate:Enabled");

    private async Task<SqlConnection> RequireTargetAsync(CancellationToken ct)
    {
        if (!Enabled) throw new InvalidOperationException("OPTIONC_CANDIDATE_DISABLED");
        await db.Database.OpenConnectionAsync(ct);
        var connection = db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("OPTIONC_SQLSERVER_REQUIRED");
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection);
        await V180OptionCPublicationCatalogRules.RequireAsync(connection, ct);
        return connection;
    }

    /// <summary>
    /// Isolated CreateLocation entry point. A stable server-owned LocationId,
    /// inactive reservation row, canonical request, Submitted event and private
    /// Stage are all committed or rolled back in ONE Serializable transaction.
    /// No published Location or version is written here.
    /// </summary>
    public Task<V180B3RequestView> CreateAsync(
        V180OptionCCreateLocation input, CancellationToken ct)
        => CreateCoreAsync(input, ct, null);

    private async Task<V180B3RequestView> CreateCoreAsync(
        V180OptionCCreateLocation input, CancellationToken ct,
        V180OptionCExcelRowSource? excel)
    {
        if (input is null || input.TeamId <= 0)
            throw new InvalidOperationException("OPTIONC_CREATE_INPUT_INVALID");
        var validated = V180B3ProposalSafetyRules.Validate(input.Proposed,
            new V180B3LocationFields("", null, null, null, null, null, null),
            input.Reason);
        var proposed = validated.Proposed;
        if (proposed.Address is null || proposed.Address.Length > 500)
            throw new InvalidOperationException("OPTIONC_CREATE_ADDRESS_REQUIRED");
        V180OptionCCreateLowRiskPolicy.ClassifyCreate(proposed.LocationName,
            proposed.Address, 1, input.TeamId);
        var connection = await RequireTargetAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var sqlTx = (SqlTransaction)tx.GetDbTransaction();
            var actorId = current.GetRequired().UserId;
            var scope = await new V180OptionCLiveAuthorizationAdapter(db)
                .AuthorizeCreateSubmissionAsync(actorId, input.TeamId, BusinessTime.Today, ct);
            FieldVisit.Domain.Entities.ImportBatchItem? excelItem = null;
            FieldVisit.Domain.Entities.ImportBatch? excelBatch = null;
            if (excel is not null)
            {
                await RequireExcelStateContractAsync(connection,sqlTx,ct);
                // Lock the preview row and its batch inside the SAME serializable
                // transaction that will commit Request/Reservation/Stage/audit.
                // A concurrent/replayed submission cannot claim the same row.
                excelBatch = await db.ImportBatches.FromSqlInterpolated(
                    $"SELECT * FROM dbo.ImportBatches WITH (UPDLOCK,HOLDLOCK) WHERE ImportBatchId={excel.ImportBatchId}")
                    .SingleOrDefaultAsync(ct);
                excelItem = await db.ImportBatchItems.FromSqlInterpolated(
                    $"SELECT * FROM dbo.ImportBatchItems WITH (UPDLOCK,HOLDLOCK) WHERE ImportBatchItemId={excel.ImportBatchItemId}")
                    .SingleOrDefaultAsync(ct);
                if (excelBatch is null || excelItem is null ||
                    excelItem.ImportBatchId != excel.ImportBatchId ||
                    excelBatch.ImportType != "locations" ||
                    excelBatch.OrganizationId != scope.OrganizationId ||
                    excelBatch.RequestedByUserId != actorId ||
                    excelBatch.ExpiresAt < DateTime.UtcNow ||
                    excelBatch.Status is not ("Previewed" or "PartiallySubmittedForReview") ||
                    excelItem.EntityType != "Location" || excelItem.Action != "Create" ||
                    excelItem.Status != "Error" ||
                    excelItem.ErrorMessage != "LOCATION_EXCEL_CREATE_REQUIRES_CANONICAL_REVIEW")
                    throw new InvalidOperationException("OPTIONC_EXCEL_ROW_NOT_ELIGIBLE");
                var staged = JsonSerializer.Deserialize<V180OptionCExcelLocationRow>(
                    excelItem.DataJson,new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("OPTIONC_EXCEL_ROW_INVALID");
                if (!string.IsNullOrWhiteSpace(staged.LocationCode) ||
                    !string.Equals(staged.LocationName.Trim(), proposed.LocationName,
                        StringComparison.Ordinal) ||
                    !string.Equals(staged.Address?.Trim(), proposed.Address,
                        StringComparison.Ordinal) ||
                    !string.Equals(staged.City?.Trim(), proposed.City,
                        StringComparison.Ordinal) ||
                    !string.Equals(staged.District?.Trim(), proposed.District,
                        StringComparison.Ordinal) ||
                    !string.Equals(staged.PlusCode?.Trim(), proposed.PlusCode,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("OPTIONC_EXCEL_ROW_PROPOSAL_DRIFT");
                var teamCode = await db.Teams.AsNoTracking()
                    .Where(x => x.TeamId == input.TeamId && x.OrganizationId == scope.OrganizationId)
                    .Select(x => x.TeamCode).SingleOrDefaultAsync(ct);
                if (string.IsNullOrWhiteSpace(teamCode) ||
                    !string.Equals(teamCode, staged.TeamCode?.Trim(),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("OPTIONC_EXCEL_TEAM_DRIFT");
            }
            await using (var schema = new SqlCommand("""
                SELECT CAST(CASE WHEN EXISTS (
                  SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')
                  AND OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NOT NULL
                  THEN 1 ELSE 0 END AS int)
                """, connection, sqlTx))
            {
                if (Convert.ToInt32(await schema.ExecuteScalarAsync(ct)) != 1)
                    throw new InvalidOperationException("OPTIONC_CREATE_SCHEMA_NOT_READY");
            }
            // The transaction-owned identity key serializes duplicate pending
            // submissions from independent HTTP requests and connections.
            var keySource = scope.OrganizationId + "|" + input.TeamId + "|"
                + proposed.LocationName.ToUpperInvariant() + "|"
                + proposed.Address.ToUpperInvariant();
            var keyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(keySource)));
            await using (var claim = new SqlCommand("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=@key,
                    @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
                SELECT @result;
                """, connection, sqlTx))
            {
                claim.Parameters.AddWithValue("@key", "OptionC.Create." + keyHash);
                if (Convert.ToInt32(await claim.ExecuteScalarAsync(ct)) < 0)
                    throw new InvalidOperationException("OPTIONC_CREATE_LOCK_DENIED");
            }
            // An abandoned/rejected canonical reservation is historical, not
            // an active submission. It must not permanently block an amended
            // new application for the same identity. Unreserved legacy drafts
            // and still-Pending reservations continue to block duplicates.
            await using (var duplicates = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.Locations l WITH(HOLDLOCK)
                WHERE l.OrganizationId=@org AND l.TeamId=@team
                  AND l.ApprovalStatus=N'Pending' AND l.IsActive=0
                  AND l.LocationName=@name AND l.Address=@address
                  AND NOT EXISTS (
                    SELECT 1 FROM dbo.LocationCreateReservations cr WITH(HOLDLOCK)
                    JOIN dbo.ChangeRequests r WITH(HOLDLOCK)
                      ON r.ChangeRequestId=cr.ChangeRequestId
                    WHERE cr.LocationId=l.LocationId
                      AND cr.OrganizationId=l.OrganizationId AND cr.TeamId=l.TeamId
                      AND cr.ReservationStatus=N'Abandoned'
                      AND r.EntityKind=N'Location'
                      AND r.OperationCode=N'CreateLocation'
                      AND r.Status=N'Rejected')
                """, connection, sqlTx))
            {
                duplicates.Parameters.AddWithValue("@org", scope.OrganizationId);
                duplicates.Parameters.AddWithValue("@team", input.TeamId);
                duplicates.Parameters.AddWithValue("@name", proposed.LocationName);
                duplicates.Parameters.AddWithValue("@address", proposed.Address);
                if (Convert.ToInt32(await duplicates.ExecuteScalarAsync(ct)) != 0)
                    throw new InvalidOperationException("OPTIONC_CREATE_PENDING_DUPLICATE");
            }

            var requestId = Guid.NewGuid();
            var code = "OC-" + requestId.ToString("N");
            // Disposable SQL fixtures use a non-IDENTITY LocationId while
            // application databases may use IDENTITY. Both allocate IDs in
            // SQL, never from a client. Non-IDENTITY fallback takes TABLOCKX.
            await using var identityCheck = new SqlCommand("""
                SELECT CAST(COLUMNPROPERTY(OBJECT_ID(N'dbo.Locations'),
                    N'LocationId',N'IsIdentity') AS int)
                """, connection, sqlTx);
            var isIdentity = Convert.ToInt32(await identityCheck.ExecuteScalarAsync(ct)) == 1;
            var insertSql = isIdentity ? """
                INSERT dbo.Locations(OrganizationId,TeamId,LocationCode,CreatedByUserId,
                    LocationType,ApprovalStatus,GeocodingStatus,IsActive,IsTemporary,
                    LocationName,Address,City,District,PlusCode,TaxId,MasterNote,CreatedAt)
                VALUES(@org,@team,@code,@user,N'Customer',N'Pending',N'Pending',0,0,
                    @name,@address,@city,@district,@plus,@tax,@note,SYSUTCDATETIME());
                SELECT CONVERT(int,SCOPE_IDENTITY());
                """ : """
                DECLARE @next int;
                SELECT @next=ISNULL(MAX(LocationId),0)+1
                    FROM dbo.Locations WITH(TABLOCKX,HOLDLOCK);
                INSERT dbo.Locations(LocationId,OrganizationId,TeamId,LocationCode,
                    CreatedByUserId,LocationType,ApprovalStatus,GeocodingStatus,
                    IsActive,IsTemporary,LocationName,Address,City,District,PlusCode,
                    TaxId,MasterNote,CreatedAt)
                VALUES(@next,@org,@team,@code,@user,N'Customer',N'Pending',N'Pending',
                    0,0,@name,@address,@city,@district,@plus,@tax,@note,SYSUTCDATETIME());
                SELECT @next;
                """;
            int locationId;
            await using (var insert = new SqlCommand(insertSql, connection, sqlTx))
            {
                insert.Parameters.AddWithValue("@org", scope.OrganizationId);
                insert.Parameters.AddWithValue("@team", input.TeamId);
                insert.Parameters.AddWithValue("@code", code);
                insert.Parameters.AddWithValue("@user", actorId);
                insert.Parameters.AddWithValue("@name", proposed.LocationName);
                insert.Parameters.AddWithValue("@address", proposed.Address);
                insert.Parameters.AddWithValue("@city", (object?)proposed.City ?? DBNull.Value);
                insert.Parameters.AddWithValue("@district", (object?)proposed.District ?? DBNull.Value);
                insert.Parameters.AddWithValue("@plus", (object?)proposed.PlusCode ?? DBNull.Value);
                insert.Parameters.AddWithValue("@tax", (object?)proposed.TaxId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@note", (object?)proposed.MasterNote ?? DBNull.Value);
                locationId = Convert.ToInt32(await insert.ExecuteScalarAsync(ct));
            }
            if (locationId <= 0)
                throw new InvalidOperationException("OPTIONC_CREATE_LOCATION_RESERVATION_FAILED");
            byte[] version;
            await using (var rv = new SqlCommand("""
                SELECT RowVersion FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK)
                WHERE LocationId=@location AND OrganizationId=@org AND TeamId=@team
                  AND CreatedByUserId=@user AND IsActive=0 AND ApprovalStatus=N'Pending'
                """, connection, sqlTx))
            {
                rv.Parameters.AddWithValue("@location", locationId);
                rv.Parameters.AddWithValue("@org", scope.OrganizationId);
                rv.Parameters.AddWithValue("@team", input.TeamId);
                rv.Parameters.AddWithValue("@user", actorId);
                version = await rv.ExecuteScalarAsync(ct) as byte[]
                    ?? throw new InvalidOperationException("OPTIONC_CREATE_VERSION_MISSING");
            }
            if (version.Length != 8)
                throw new InvalidOperationException("OPTIONC_CREATE_VERSION_INVALID");
            var row = new V180B3ChangeRequest
            {
                RequestPublicId=requestId, OrganizationId=scope.OrganizationId,
                TeamId=input.TeamId, EntityKind="Location", EntityId=locationId.ToString(),
                OperationCode="CreateLocation", RiskCode="High",
                ExpectedEntityRowVersion=version, BeforeJson=null,
                ProposedJson=JsonSerializer.Serialize(proposed),
                EvidenceJson=JsonSerializer.Serialize(new { Source="LiveHR",
                    Operation="CreateLocation", scope.VisitorEligible, scope.LeaderEligible }),
                RequestedByUserId=actorId, SubmittedAt=DateTime.UtcNow, Status="Pending"
            };
            db.ChangeRequests.Add(row);
            await db.SaveChangesAsync(ct);
            await using (var reservation = new SqlCommand("""
                INSERT dbo.LocationCreateReservations(ChangeRequestId,LocationId,
                    OrganizationId,TeamId,ReservedLocationRowVersion,
                    ReservationStatus,CreatedAt,UpdatedAt)
                VALUES(@request,@location,@org,@team,@version,N'Pending',
                    SYSUTCDATETIME(),SYSUTCDATETIME());
                SELECT CONVERT(int,@@ROWCOUNT);
                """, connection, sqlTx))
            {
                reservation.Parameters.AddWithValue("@request", row.ChangeRequestId);
                reservation.Parameters.AddWithValue("@location", locationId);
                reservation.Parameters.AddWithValue("@org", scope.OrganizationId);
                reservation.Parameters.AddWithValue("@team", input.TeamId);
                reservation.Parameters.AddWithValue("@version", version);
                if (Convert.ToInt32(await reservation.ExecuteScalarAsync(ct)) != 1)
                    throw new InvalidOperationException("OPTIONC_CREATE_RESERVATION_FAILED");
            }
            db.ChangeRequestEvents.Add(new V180B3ChangeEvent {
                ChangeRequestId=row.ChangeRequestId, EventType="Submitted",
                ActorUserId=actorId, OccurredAt=DateTime.UtcNow,
                CorrelationId=Guid.NewGuid(),
                DetailsJson=JsonSerializer.Serialize(new { Reason=validated.Reason,
                    LocationId=locationId, Operation="CreateLocation",
                    Source=excel is null ? "HTTP" : "Excel",
                    ImportBatchId=excel?.ImportBatchId,
                    ImportBatchItemId=excel?.ImportBatchItemId })
            });
            await db.SaveChangesAsync(ct);
            await V180OptionCStageDraftWriter.CreateFromLockedRequestAsync(
                connection, sqlTx, requestId, scope.OrganizationId, ct);
            if (excelItem is not null && excelBatch is not null)
            {
                // This row marker and the Submitted audit are atomic with the
                // reserved Location and private Stage. Never use a separate
                // commit for the Excel-to-canonical reference.
                excelItem.Status = "SubmittedForReview";
                excelItem.ErrorMessage = null;
                excelBatch.Status = "PartiallySubmittedForReview";
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
            return ToView(row);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    private static async Task RequireExcelStateContractAsync(
        SqlConnection connection, SqlTransaction? transaction, CancellationToken ct)
    {
        // No silent upgrade of 1.6 ImportBatch CHECK constraints: the
        // isolated additive status-contract marker and BOTH enabled
        // constraints must exist before any Excel submission transaction.
        await using var cmd = new SqlCommand("""
            SELECT CASE WHEN
              EXISTS(SELECT 1 FROM dbo.SchemaVersions
                WHERE VersionNumber=N'1.8.0-EXCEL-CANONICAL-STATUS')
              AND OBJECT_ID(N'dbo.ImportBatches',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.ImportBatchItems',N'U') IS NOT NULL
              AND EXISTS(SELECT 1 FROM sys.check_constraints
                WHERE parent_object_id=OBJECT_ID(N'dbo.ImportBatches')
                  AND name=N'CK_ImportBatches_Status' AND is_disabled=0
                  AND definition LIKE N'%SubmittedForReview%')
              AND EXISTS(SELECT 1 FROM sys.check_constraints
                WHERE parent_object_id=OBJECT_ID(N'dbo.ImportBatchItems')
                  AND name=N'CK_ImportBatchItems_Status' AND is_disabled=0
                  AND definition LIKE N'%SubmittedForReview%')
              THEN 1 ELSE 0 END
            """,connection,transaction);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) != 1)
            throw new InvalidOperationException("OPTIONC_EXCEL_STATUS_CONTRACT_NOT_READY");
    }

    /// <summary>
    /// Submit staged Excel Create rows as distinct canonical HighRisk
    /// requests. Each row is atomic and retry-safe; mixed Update/create
    /// batches are refused rather than falling through legacy Confirm.
    /// Cannot run on UAT/Production because RequireTargetAsync rejects
    /// non-loopback or non-OptionCIsolated SQL and disabled flags.
    /// </summary>
    public async Task<V180OptionCExcelCreateResult> SubmitExcelCreatesAsync(
        Guid importBatchId, CancellationToken ct)
    {
        if (importBatchId == Guid.Empty)
            throw new InvalidOperationException("OPTIONC_EXCEL_BATCH_REQUIRED");
        var connection = await RequireTargetAsync(ct);
        await RequireExcelStateContractAsync(connection,null,ct);
        var user = current.GetRequired();
        if (!user.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)
            || x.Equals("leader", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("OPTIONC_EXCEL_ROLE_DENIED");
        var batch = await db.ImportBatches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ImportBatchId == importBatchId, ct)
            ?? throw new KeyNotFoundException("OPTIONC_EXCEL_BATCH_NOT_FOUND");
        if (batch.RequestedByUserId != user.UserId ||
            batch.OrganizationId != user.OrganizationId ||
            batch.ImportType != "locations" ||
            batch.Status is not ("Previewed" or "PartiallySubmittedForReview" or "SubmittedForReview"))
            throw new UnauthorizedAccessException("OPTIONC_EXCEL_BATCH_SCOPE_DENIED");
        if (batch.ExpiresAt < DateTime.UtcNow && batch.Status != "SubmittedForReview")
            throw new InvalidOperationException("OPTIONC_EXCEL_BATCH_EXPIRED");
        var items = await db.ImportBatchItems.AsNoTracking()
            .Where(x => x.ImportBatchId == importBatchId)
            .OrderBy(x => x.RowNumber).ToListAsync(ct);
        if (items.Count == 0 || !items.Any(x => x.Action == "Create"))
            throw new InvalidOperationException("OPTIONC_EXCEL_CREATE_ROWS_REQUIRED");
        // Preflight the whole batch before starting even one canonical request.
        // Mixed Update rows, unknown Errors, missing addresses and invalid
        // team codes never turn into a partial direct write.
        var ready = new List<(FieldVisit.Domain.Entities.ImportBatchItem Item,
            V180OptionCExcelLocationRow Row, int TeamId)>();
        foreach (var item in items)
        {
            if (item.EntityType != "Location")
                throw new InvalidOperationException("OPTIONC_EXCEL_MIXED_ENTITY_DENIED");
            if (item.Action == "NoChange" && item.Status == "Valid") continue;
            if (item.Action != "Create" ||
                !(item.Status == "SubmittedForReview" ||
                  (item.Status == "Error" &&
                   item.ErrorMessage == "LOCATION_EXCEL_CREATE_REQUIRES_CANONICAL_REVIEW")))
                throw new InvalidOperationException("OPTIONC_EXCEL_MIXED_OR_INVALID_ROW_DENIED");
            var row = JsonSerializer.Deserialize<V180OptionCExcelLocationRow>(
                item.DataJson,new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("OPTIONC_EXCEL_ROW_INVALID");
            if (!string.IsNullOrWhiteSpace(row.LocationCode) ||
                string.IsNullOrWhiteSpace(row.TeamCode) ||
                string.IsNullOrWhiteSpace(row.LocationName) ||
                string.IsNullOrWhiteSpace(row.Address))
                throw new InvalidOperationException("OPTIONC_EXCEL_REQUIRED_FIELDS_MISSING");
            var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TeamCode == row.TeamCode.Trim() &&
                x.OrganizationId == batch.OrganizationId && x.IsActive, ct)
                ?? throw new UnauthorizedAccessException("OPTIONC_EXCEL_TEAM_SCOPE_DENIED");
            ready.Add((item,row,team.TeamId));
        }
        var requests = new List<Guid>();
        int submitted = 0, already = 0;
        foreach (var (item,row,teamId) in ready)
        {
            if (item.Status == "SubmittedForReview")
            {
                // Authoritative link is the canonical Submitted audit.
                // No new identity may be created on a replay.
                requests.Add(await FindExcelSubmissionAsync(connection,
                    importBatchId,item.ImportBatchItemId,ct));
                already++;
                continue;
            }
            var fields = new V180B3LocationFields(row.LocationName.Trim(),
                row.City?.Trim(),row.District?.Trim(),row.Address?.Trim(),
                row.PlusCode?.Trim(),null,null);
            var request = await CreateCoreAsync(new V180OptionCCreateLocation(
                teamId,$"Excel batch {importBatchId:N}, row {item.RowNumber}: independent review required",
                fields), ct,
                new V180OptionCExcelRowSource(importBatchId,item.ImportBatchItemId));
            requests.Add(request.RequestPublicId);
            submitted++;
        }
        var latest = await db.ImportBatchItems.AsNoTracking()
            .Where(x => x.ImportBatchId == importBatchId && x.Action == "Create")
            .Select(x => x.Status).ToListAsync(ct);
        if (latest.Count != ready.Count || latest.Any(x => x != "SubmittedForReview"))
            throw new InvalidOperationException("OPTIONC_EXCEL_PARTIAL_SUBMISSION_REQUIRES_RETRY");
        var finished = await db.ImportBatches.SingleAsync(
            x => x.ImportBatchId == importBatchId, ct);
        finished.Status = "SubmittedForReview";
        finished.ConfirmedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new(importBatchId,submitted,already,"SubmittedForReview",requests);
    }

    /// <summary>
    /// Excel published Update rows use the SAME canonical submission,
    /// Leader-first review, evidence and atomic publication as HTTP updates.
    /// Explicitly fail closed for mixed Create/Update batches until the
    /// mixed-operation contract is verified. No direct Confirm writes.
    /// </summary>
    public async Task<V180OptionCExcelCreateResult> SubmitExcelUpdatesAsync(
        Guid importBatchId, CancellationToken ct)
    {
        if (importBatchId == Guid.Empty)
            throw new InvalidOperationException("OPTIONC_EXCEL_BATCH_REQUIRED");
        var connection = await RequireTargetAsync(ct);
        await RequireExcelStateContractAsync(connection,null,ct);
        var user = current.GetRequired();
        if (!user.Roles.Any(x => x.Equals("admin",StringComparison.OrdinalIgnoreCase)
            || x.Equals("leader",StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("OPTIONC_EXCEL_ROLE_DENIED");
        var batch = await db.ImportBatches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ImportBatchId == importBatchId,ct)
            ?? throw new KeyNotFoundException("OPTIONC_EXCEL_BATCH_NOT_FOUND");
        if (batch.RequestedByUserId != user.UserId
            || batch.OrganizationId != user.OrganizationId
            || batch.ImportType != "locations"
            || batch.Status is not ("Previewed" or "PartiallySubmittedForReview" or "SubmittedForReview"))
            throw new UnauthorizedAccessException("OPTIONC_EXCEL_BATCH_SCOPE_DENIED");
        if (batch.ExpiresAt < DateTime.UtcNow && batch.Status != "SubmittedForReview")
            throw new InvalidOperationException("OPTIONC_EXCEL_BATCH_EXPIRED");
        var items = await db.ImportBatchItems.AsNoTracking()
            .Where(x => x.ImportBatchId == importBatchId)
            .OrderBy(x => x.RowNumber).ToListAsync(ct);
        if (items.Count == 0 || !items.Any(x => x.Action == "Update"))
            throw new InvalidOperationException("OPTIONC_EXCEL_UPDATE_ROWS_REQUIRED");
        var ready = new List<(FieldVisit.Domain.Entities.ImportBatchItem Item,
            V180OptionCExcelLocationRow Row, FieldVisit.Domain.Entities.Location Location)>();
        foreach (var item in items)
        {
            if (item.EntityType != "Location")
                throw new InvalidOperationException("OPTIONC_EXCEL_MIXED_ENTITY_DENIED");
            if (item.Action == "NoChange" && item.Status == "Valid") continue;
            if (item.Action != "Update"
                || !(item.Status == "SubmittedForReview"
                    || (item.Status == "Error" &&
                        item.ErrorMessage == "LOCATION_EXCEL_PUBLISHED_CHANGE_REQUIRES_UNIFIED_APPROVAL")))
                throw new InvalidOperationException("OPTIONC_EXCEL_MIXED_OR_INVALID_ROW_DENIED");
            var row = JsonSerializer.Deserialize<V180OptionCExcelLocationRow>(
                item.DataJson,new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("OPTIONC_EXCEL_UPDATE_ROW_INVALID");
            if (string.IsNullOrWhiteSpace(row.LocationCode)
                || string.IsNullOrWhiteSpace(row.TeamCode)
                || string.IsNullOrWhiteSpace(row.LocationName)
                || string.IsNullOrWhiteSpace(row.ExpectedEntityRowVersion))
                throw new InvalidOperationException("OPTIONC_EXCEL_UPDATE_REQUIRED_FIELDS_MISSING");
            // The rowversion is captured at preview, not invented at submit.
            _ = V180B3RowVersionRules.Parse(row.ExpectedEntityRowVersion);
            var location = await db.Locations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OrganizationId == batch.OrganizationId
                    && x.LocationCode == row.LocationCode.Trim(),ct)
                ?? throw new UnauthorizedAccessException("OPTIONC_EXCEL_UPDATE_LOCATION_NOT_FOUND");
            if (location.TeamId is not int teamId
                || location.LocationType != "Customer"
                || location.ApprovalStatus != "Approved" || !location.IsActive
                || !await db.Teams.AsNoTracking().AnyAsync(x =>
                    x.TeamId == teamId && x.OrganizationId == batch.OrganizationId
                    && x.IsActive && x.TeamCode == row.TeamCode.Trim(),ct))
                throw new UnauthorizedAccessException("OPTIONC_EXCEL_UPDATE_LOCATION_SCOPE_DENIED");
            ready.Add((item,row,location));
        }
        var ids = new List<Guid>();
        int submitted=0,already=0;
        foreach (var (item,row,location) in ready)
        {
            if (item.Status == "SubmittedForReview")
            {
                ids.Add(await FindExcelSubmissionAsync(connection,
                    importBatchId,item.ImportBatchItemId,ct));
                already++;
                continue;
            }
            var proposal = new V180B3LocationFields(
                row.LocationName.Trim(),row.City?.Trim(),row.District?.Trim(),
                row.Address?.Trim(),row.PlusCode?.Trim(),
                location.TaxId,location.MasterNote);
            var request = await SubmitCoreAsync(new V180B3SubmitLocation(
                location.LocationId,row.ExpectedEntityRowVersion!,
                $"Excel batch {importBatchId:N}, row {item.RowNumber}: independent review required",
                proposal),ct,new V180OptionCExcelRowSource(importBatchId,item.ImportBatchItemId));
            ids.Add(request.RequestPublicId);
            submitted++;
        }
        var statuses = await db.ImportBatchItems.AsNoTracking()
            .Where(x => x.ImportBatchId == importBatchId && x.Action == "Update")
            .Select(x => x.Status).ToListAsync(ct);
        if (statuses.Count != ready.Count || statuses.Any(x => x != "SubmittedForReview"))
            throw new InvalidOperationException("OPTIONC_EXCEL_PARTIAL_SUBMISSION_REQUIRES_RETRY");
        var finished = await db.ImportBatches.SingleAsync(
            x => x.ImportBatchId == importBatchId,ct);
        finished.Status="SubmittedForReview";
        finished.ConfirmedAt=DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new(importBatchId,submitted,already,"SubmittedForReview",ids);
    }

    private static async Task<Guid> FindExcelSubmissionAsync(
        SqlConnection connection, Guid batch, long item, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT r.RequestPublicId
            FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
            WHERE e.EventType=N'Submitted'
              AND JSON_VALUE(e.DetailsJson,N'$.Source')=N'Excel'
              AND TRY_CONVERT(uniqueidentifier,
                  JSON_VALUE(e.DetailsJson,N'$.ImportBatchId'))=@batch
              AND TRY_CONVERT(bigint,
                  JSON_VALUE(e.DetailsJson,N'$.ImportBatchItemId'))=@item
            """,connection);
        cmd.Parameters.AddWithValue("@batch",batch);
        cmd.Parameters.AddWithValue("@item",item);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        if (ids.Count != 1)
            throw new InvalidOperationException("OPTIONC_EXCEL_CANONICAL_AUDIT_LINK_MISSING");
        return ids[0];
    }

    public Task<V180B3RequestView> SubmitAsync(V180B3SubmitLocation input, CancellationToken ct)
        => SubmitCoreAsync(input, ct, null);

    private async Task<V180B3RequestView> SubmitCoreAsync(
        V180B3SubmitLocation input, CancellationToken ct, V180OptionCExcelRowSource? excel)
    {
        var expected = V180B3RequestInputRules.RequireSubmission(input);
        var connection = await RequireTargetAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var actor = current.GetRequired();
            var actorId = actor.UserId;
            var auth = new V180OptionCLiveAuthorizationAdapter(db);
            var scope = await auth.AuthorizeSubmissionAsync(actorId, input.LocationId, BusinessTime.Today, ct);
            FieldVisit.Domain.Entities.ImportBatch? excelBatch = null;
            FieldVisit.Domain.Entities.ImportBatchItem? excelItem = null;
            if (excel is not null)
            {
                await RequireExcelStateContractAsync(connection,
                    (SqlTransaction)tx.GetDbTransaction(),ct);
                excelBatch = await db.ImportBatches.FromSqlInterpolated(
                    $"SELECT * FROM dbo.ImportBatches WITH(UPDLOCK,HOLDLOCK) WHERE ImportBatchId={excel.ImportBatchId}")
                    .SingleOrDefaultAsync(ct);
                excelItem = await db.ImportBatchItems.FromSqlInterpolated(
                    $"SELECT * FROM dbo.ImportBatchItems WITH(UPDLOCK,HOLDLOCK) WHERE ImportBatchItemId={excel.ImportBatchItemId}")
                    .SingleOrDefaultAsync(ct);
                if (excelBatch is null || excelItem is null
                    || excelItem.ImportBatchId != excel.ImportBatchId
                    || excelBatch.RequestedByUserId != actorId
                    || excelBatch.OrganizationId != scope.OrganizationId
                    || excelBatch.ImportType != "locations"
                    || excelBatch.ExpiresAt < DateTime.UtcNow
                    || excelBatch.Status is not ("Previewed" or "PartiallySubmittedForReview")
                    || excelItem.EntityType != "Location" || excelItem.Action != "Update"
                    || excelItem.Status != "Error"
                    || excelItem.ErrorMessage != "LOCATION_EXCEL_PUBLISHED_CHANGE_REQUIRES_UNIFIED_APPROVAL")
                    throw new InvalidOperationException("OPTIONC_EXCEL_UPDATE_ROW_NOT_ELIGIBLE");
                var staged = JsonSerializer.Deserialize<V180OptionCExcelLocationRow>(
                    excelItem.DataJson,new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("OPTIONC_EXCEL_UPDATE_ROW_INVALID");
                if (staged.ExpectedEntityRowVersion != input.ExpectedRowVersion
                    || string.IsNullOrWhiteSpace(staged.LocationCode)
                    || !string.Equals(staged.LocationName.Trim(),input.Proposed.LocationName,StringComparison.Ordinal)
                    || !string.Equals(staged.City?.Trim(),input.Proposed.City,StringComparison.Ordinal)
                    || !string.Equals(staged.District?.Trim(),input.Proposed.District,StringComparison.Ordinal)
                    || !string.Equals(staged.Address?.Trim(),input.Proposed.Address,StringComparison.Ordinal)
                    || !string.Equals(staged.PlusCode?.Trim(),input.Proposed.PlusCode,StringComparison.Ordinal))
                    throw new InvalidOperationException("OPTIONC_EXCEL_UPDATE_PROPOSAL_DRIFT");
            }
            var location = await db.Locations.FromSqlInterpolated($"""
                SELECT * FROM dbo.Locations WITH (UPDLOCK,HOLDLOCK)
                WHERE LocationId={input.LocationId} AND OrganizationId={scope.OrganizationId}
                """).SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("OPTIONC_LOCATION_NOT_FOUND");
            if (location.RowVersion is null || !location.RowVersion.SequenceEqual(expected))
                throw new InvalidOperationException("ROWVERSION_CONFLICT");
            if (excelItem is not null)
            {
                var staged = JsonSerializer.Deserialize<V180OptionCExcelLocationRow>(
                    excelItem.DataJson,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                var teamCode = await db.Teams.AsNoTracking()
                    .Where(x => x.TeamId == location.TeamId
                        && x.OrganizationId == scope.OrganizationId && x.IsActive)
                    .Select(x => x.TeamCode).SingleOrDefaultAsync(ct);
                if (string.IsNullOrWhiteSpace(teamCode)
                    || !string.Equals(staged.TeamCode?.Trim(),teamCode,StringComparison.Ordinal)
                    || !string.Equals(staged.LocationCode?.Trim(),location.LocationCode,StringComparison.Ordinal)
                    || location.ApprovalStatus != "Approved" || !location.IsActive
                    || location.LocationType != "Customer")
                    throw new UnauthorizedAccessException("OPTIONC_EXCEL_UPDATE_LOCATION_SCOPE_DRIFT");
            }
            var before = new V180B3LocationFields(location.LocationName, location.City,
                location.District, location.Address, location.PlusCode, location.TaxId, location.MasterNote);
            var proposal = V180B3ProposalSafetyRules.Validate(input.Proposed, before, input.Reason);
            // The server, not the caller, owns risk classification. A Low
            // request is allowed only with the additive isolated SQL contract.
            var risk = V180OptionCCreateLowRiskPolicy.ClassifyPublishedUpdate(
                before.City, before.District, before.Address, before.PlusCode, before.TaxId,
                proposal.Proposed.City, proposal.Proposed.District, proposal.Proposed.Address,
                proposal.Proposed.PlusCode, proposal.Proposed.TaxId,
                published: true, changedAnyField: true);
            if (risk == V180OptionCCreateLowRiskPolicy.Low)
            {
                await using var gate = new SqlCommand("""
                    SELECT COUNT(*) FROM dbo.SchemaVersions
                    WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK'
                    """, connection, (SqlTransaction)tx.GetDbTransaction());
                if (Convert.ToInt32(await gate.ExecuteScalarAsync(ct)) != 1)
                    throw new InvalidOperationException("OPTIONC_LOW_SCHEMA_NOT_READY");
            }
            if (await db.ChangeRequests.AnyAsync(x => x.OrganizationId == scope.OrganizationId
                && x.EntityKind == "Location" && x.EntityId == input.LocationId.ToString()
                && x.Status == "Pending", ct))
                throw new InvalidOperationException("B3_PENDING_REQUEST_EXISTS");
            var row = new V180B3ChangeRequest {
                RequestPublicId = Guid.NewGuid(), OrganizationId = scope.OrganizationId,
                TeamId = scope.TeamId, EntityKind = "Location", EntityId = input.LocationId.ToString(),
                OperationCode = "UpdatePublishedLocation", RiskCode = risk,
                ExpectedEntityRowVersion = location.RowVersion.ToArray(),
                BeforeJson = JsonSerializer.Serialize(before), ProposedJson = JsonSerializer.Serialize(proposal.Proposed),
                EvidenceJson = JsonSerializer.Serialize(new { Source = "LiveHR", scope.VisitorEligible, scope.LeaderEligible }),
                RequestedByUserId = actorId, SubmittedAt = DateTime.UtcNow, Status = "Pending" };
            db.ChangeRequests.Add(row);
            await db.SaveChangesAsync(ct);
            db.ChangeRequestEvents.Add(new V180B3ChangeEvent {
                ChangeRequestId = row.ChangeRequestId, EventType = "Submitted", ActorUserId = actorId,
                OccurredAt = DateTime.UtcNow, CorrelationId = Guid.NewGuid(),
                DetailsJson = JsonSerializer.Serialize(new { Reason = proposal.Reason,
                    Source = excel is null ? "HTTP" : "Excel",
                    ImportBatchId = excel?.ImportBatchId,
                    ImportBatchItemId = excel?.ImportBatchItemId }) });
            await db.SaveChangesAsync(ct);
            var sqlTx = tx.GetDbTransaction() as SqlTransaction
                ?? throw new InvalidOperationException("OPTIONC_SQL_TRANSACTION_REQUIRED");
            await V180OptionCStageDraftWriter.CreateFromLockedRequestAsync(
                connection, sqlTx, row.RequestPublicId, scope.OrganizationId, ct);
            if (excelItem is not null && excelBatch is not null)
            {
                excelItem.Status = "SubmittedForReview";
                excelItem.ErrorMessage = null;
                excelBatch.Status = "PartiallySubmittedForReview";
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
            return ToView(row);
        }
        catch { try { await tx.RollbackAsync(CancellationToken.None); } catch { } throw; }
    }

    public async Task<IReadOnlyList<V180B3RequestView>> OwnAsync(CancellationToken ct)
    {
        await RequireTargetAsync(ct);
        var actorId = current.GetRequired().UserId;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var org = await new V180OptionCLiveAuthorizationAdapter(db).ResolveLiveOrganizationAsync(actorId, ct);
        var rows = await db.ChangeRequests.AsNoTracking().Where(x => x.OrganizationId == org
            && x.RequestedByUserId == actorId && x.EntityKind == "Location"
            && (x.OperationCode == "UpdatePublishedLocation" || x.OperationCode == "CreateLocation")
            && (x.RiskCode == "High" || (x.OperationCode == "UpdatePublishedLocation" && x.RiskCode == "Low"))
            && (x.Status == "Pending" || x.Status == "Rejected" || x.Status == "Applied"))
            .OrderByDescending(x => x.SubmittedAt).Take(100).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return rows.Select(ToView).ToList();
    }

    public async Task<V180B3RequestView> StatusAsync(Guid id, CancellationToken ct)
    {
        await RequireTargetAsync(ct);
        var actorId = current.GetRequired().UserId;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var org = await new V180OptionCLiveAuthorizationAdapter(db)
            .ResolveLiveOrganizationAsync(actorId, ct);
        var row = await db.ChangeRequests.AsNoTracking().SingleOrDefaultAsync(x =>
            x.RequestPublicId == id && x.OrganizationId == org
            && x.RequestedByUserId == actorId
            && x.EntityKind == "Location"
            && (x.OperationCode == "UpdatePublishedLocation" || x.OperationCode == "CreateLocation")
            && (x.RiskCode == "High" || (x.OperationCode == "UpdatePublishedLocation" && x.RiskCode == "Low")), ct)
            ?? throw new UnauthorizedAccessException("OPTIONC_REQUEST_SCOPE_DENIED");
        await tx.CommitAsync(ct);
        return ToView(row);
    }

    public async Task<IReadOnlyList<V180B3RequestView>> QueueAsync(CancellationToken ct)
    {
        await RequireTargetAsync(ct);
        var actorId = current.GetRequired().UserId;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var org = await new V180OptionCLiveAuthorizationAdapter(db)
            .RequireIndependentAdminAsync(actorId, BusinessTime.Today, ct);
        var rows = await db.ChangeRequests.AsNoTracking().Where(x => x.OrganizationId == org
            && x.EntityKind == "Location"
            && (x.OperationCode == "UpdatePublishedLocation" || x.OperationCode == "CreateLocation")
            && x.RiskCode == "High" && x.Status == "Pending")
            .OrderBy(x => x.SubmittedAt).Take(100).ToListAsync(ct);
        var visible = new List<V180B3ChangeRequest>();
        foreach (var row in rows)
        {
            if (row.TeamId is not int team) continue;
            try
            {
                await new V180OptionCLiveAuthorizationAdapter(db).AuthorizeDecisionAsync(
                    row.RequestedByUserId, actorId, org, team, BusinessTime.Today, ct);
                visible.Add(row);
            }
            catch (UnauthorizedAccessException) { }
        }
        await tx.CommitAsync(ct);
        return visible.Select(ToView).ToList();
    }

    public async Task<V180B3RequestView> ApproveAsync(Guid id, V180B3Review input, CancellationToken ct)
    {
        V180B3RequestInputRules.RequireReviewTarget(id, input);
        await RequireTargetAsync(ct);
        await new V180OptionCAtomicPublicationOrchestrator(db, current)
            .ExecuteAsync(id, input.DecisionKey, input.Reason, ct);
        return await FindAsync(id, ct);
    }

    /// <summary>
    /// Candidate-only CreateLocation/High and UpdatePublishedLocation/Low
    /// publication. No deployed feature flag or production DB can enable it:
    /// RequireTargetAsync enforces explicit candidate setting and disposable
    /// local SQL Server, while the orchestrator revalidates live authority.
    /// </summary>
    public async Task<V180B3RequestView> PublishReviewedAsync(
        Guid id, V180B3Review input, CancellationToken ct)
    {
        V180B3RequestInputRules.RequireReviewTarget(id, input);
        await RequireTargetAsync(ct);
        await new V180OptionCCreateLowRiskPublicationOrchestrator(db, current)
            .ExecuteAsync(id, input.DecisionKey, input.Reason,
                input.RequestRowVersion, ct);
        return await FindAsync(id, ct);
    }

    public async Task<V180B3RequestView> RejectAsync(Guid id, V180B3Review input, CancellationToken ct)
    {
        V180B3RequestInputRules.RequireReviewTarget(id, input);
        var connection = await RequireTargetAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var reviewer = current.GetRequired().UserId;
        var auth = new V180OptionCLiveAuthorizationAdapter(db);
        var org = await auth.ResolveLiveOrganizationAsync(reviewer, ct);
        var request = await db.ChangeRequests.FromSqlInterpolated(
            V180B3ApprovalLockSql.RequestByPublicId(id, org)).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
        if (request.TeamId is not int team || request.RequestedByUserId == reviewer
            || request.EntityKind != "Location" || request.Status != "Pending"
            || request.RiskCode != "High"
            || request.OperationCode is not ("UpdatePublishedLocation" or "CreateLocation" or "MergeLocationFields"))
            throw new UnauthorizedAccessException("OPTIONC_REVIEW_SCOPE_DENIED");
        // Admin is the final reviewer of HIGH-risk requests only. Low-risk
        // rejection belongs to an independent live Leader; the Admin route
        // must never bypass the Leader-first review contract.
        // MergeFields uses the existing distinct merge approval workflow.
        // Keep its Admin rejection path intact; it does not publish anything.
        // Non-merge Create/Update requests require independent Leader-first
        // approval even for a final Admin rejection.
        await using var mergeCheck = new SqlCommand("""
            SELECT CASE WHEN EXISTS(
                SELECT 1 FROM dbo.LocationMergeDetails
                WHERE ChangeRequestId=@id) THEN 1 ELSE 0 END
            """, connection, (SqlTransaction)tx.GetDbTransaction());
        mergeCheck.Parameters.AddWithValue("@id", request.ChangeRequestId);
        var isMerge = Convert.ToInt32(await mergeCheck.ExecuteScalarAsync(ct)) == 1;
        if (!isMerge)
        {
        var leader = await db.LeaderReviews.AsNoTracking().SingleOrDefaultAsync(
            x => x.ChangeRequestId == request.ChangeRequestId
              && x.ReviewStage == "LeaderFirstLevel", ct);
        if (leader?.Status != "Approved" || leader.ReviewerUserId is not int leaderId
            || leaderId == reviewer || leaderId == request.RequestedByUserId
            || leader.DecisionKey is not Guid leaderKey
            || !await db.ChangeRequestEvents.AsNoTracking().AnyAsync(
                x => x.ChangeRequestId == request.ChangeRequestId
                  && x.EventType == "LeaderApproved"
                  && x.ActorUserId == leaderId && x.DecisionKey == leaderKey, ct))
            throw new UnauthorizedAccessException("OPTIONC_ADMIN_REJECT_REQUIRES_LEADER_APPROVAL");
        var liveLeader = await auth.AuthorizeLeaderReviewAsync(
            request.RequestedByUserId, leaderId, request.OrganizationId,
            team, BusinessTime.Today, ct);
        if (!liveLeader.RequesterEligible || !liveLeader.ReviewerLeaderEligible
            || !liveLeader.OrganizationMatches || !liveLeader.TeamMatches)
            throw new UnauthorizedAccessException("OPTIONC_ADMIN_REJECT_LEADER_REVOKED");
        }
        var evidence = await auth.AuthorizeDecisionAsync(request.RequestedByUserId, reviewer,
            request.OrganizationId, team, BusinessTime.Today, ct);
        if (!evidence.OrganizationMatches || !evidence.TeamMatches)
            throw new UnauthorizedAccessException("OPTIONC_REVIEW_SCOPE_DENIED");
        if (await db.ChangeRequestEvents.AnyAsync(x => x.DecisionKey == input.DecisionKey, ct))
            throw new InvalidOperationException("OPTIONC_DECISION_REPLAY");
        var reason = V180B3ProposalSafetyRules.RequireIndependentReview(
            request.RequestedByUserId, reviewer, request.Status, request.RowVersion,
            input.RequestRowVersion, input.DecisionKey, input.Reason);
        request.Status = "Rejected"; request.ReviewedByUserId = reviewer;
        request.ReviewedAt = DateTime.UtcNow; request.ReviewReason = reason;
        db.ChangeRequestEvents.Add(new V180B3ChangeEvent {
            ChangeRequestId = request.ChangeRequestId, EventType = "Rejected", ActorUserId = reviewer,
            OccurredAt = DateTime.UtcNow, CorrelationId = Guid.NewGuid(), DecisionKey = input.DecisionKey,
            DetailsJson = JsonSerializer.Serialize(new { Reason = reason }) });
        var sqlTx = tx.GetDbTransaction() as SqlTransaction
            ?? throw new InvalidOperationException("OPTIONC_SQL_TRANSACTION_REQUIRED");
        await using (var stage = new SqlCommand("""
            UPDATE dbo.LocationPublicationStages
            SET StageStatus=N'Abandoned',UpdatedAt=SYSUTCDATETIME()
            WHERE ChangeRequestId=@id
              AND StageStatus IN(N'PendingGeocode',N'PendingDuplicateReview',N'ReadyForPublish');
            SELECT CONVERT(int,@@ROWCOUNT);
            """, connection, sqlTx))
        {
            stage.Parameters.AddWithValue("@id", request.ChangeRequestId);
            if (Convert.ToInt32(await stage.ExecuteScalarAsync(ct)) != 1)
                throw new InvalidOperationException("OPTIONC_REJECT_STAGE_CONFLICT");
        }
        if (request.OperationCode == "CreateLocation")
        {
            await using var reservation = new SqlCommand("""
                UPDATE dbo.LocationCreateReservations
                SET ReservationStatus=N'Abandoned',UpdatedAt=SYSUTCDATETIME()
                WHERE ChangeRequestId=@request AND ReservationStatus=N'Pending';
                SELECT CONVERT(int,@@ROWCOUNT);
                """, connection, sqlTx);
            reservation.Parameters.AddWithValue("@request", request.ChangeRequestId);
            if (Convert.ToInt32(await reservation.ExecuteScalarAsync(ct)) != 1)
                throw new InvalidOperationException("OPTIONC_ADMIN_REJECT_RESERVATION_CONFLICT");
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToView(request);
    }

    public async Task<string> ReconcileAsync(Guid id, Guid decisionKey, CancellationToken ct)
    {
        if (decisionKey == Guid.Empty)
            throw new InvalidOperationException("OPTIONC_DECISION_KEY_REQUIRED");
        var connection = await RequireTargetAsync(ct);
        var freshConnectionString = config.GetConnectionString("DefaultConnection")
            ?? connection.ConnectionString;
        var actorId = current.GetRequired().UserId;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var auth = new V180OptionCLiveAuthorizationAdapter(db);
            var org = await auth.ResolveLiveOrganizationAsync(actorId, ct);
            var request = await db.ChangeRequests.FromSqlInterpolated(
                V180B3ApprovalLockSql.RequestByPublicId(id, org)).AsNoTracking()
                .SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
            if (request.TeamId is not int team || request.RequestedByUserId == actorId)
                throw new UnauthorizedAccessException("OPTIONC_RECONCILE_SCOPE_DENIED");
            if (request.OperationCode == "UpdatePublishedLocation" && request.RiskCode == "Low")
            {
                var leader = await auth.AuthorizeLeaderReviewAsync(
                    request.RequestedByUserId, actorId, org, team, BusinessTime.Today, ct);
                var review = await db.LeaderReviews.AsNoTracking().SingleOrDefaultAsync(
                    x => x.ChangeRequestId == request.ChangeRequestId
                      && x.ReviewStage == "LeaderFirstLevel", ct);
                if (!leader.RequesterEligible || !leader.ReviewerLeaderEligible
                    || !leader.OrganizationMatches || !leader.TeamMatches
                    || review?.Status != "Approved" || review.ReviewerUserId != actorId)
                    throw new UnauthorizedAccessException("OPTIONC_RECONCILE_SCOPE_DENIED");
            }
            else
            {
                var evidence = await auth.AuthorizeDecisionAsync(
                    request.RequestedByUserId, actorId,
                    request.OrganizationId, team, BusinessTime.Today, ct);
                if (!evidence.RequesterEligible || !evidence.IndependentAdminEligible
                    || !evidence.OrganizationMatches || !evidence.TeamMatches)
                    throw new UnauthorizedAccessException("OPTIONC_RECONCILE_SCOPE_DENIED");
            }
            await tx.CommitAsync(ct);
        }
        // Observe using a fresh non-pooled connection after authorization so the
        // read cannot inherit stale transaction state or retry across COMMIT.
        return await V180OptionCUnknownCommitReconciler.ObserveAsync(
            freshConnectionString, id, decisionKey, actorId, ct);
    }

    private async Task<V180B3RequestView> FindAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var org = await new V180OptionCLiveAuthorizationAdapter(db)
            .ResolveLiveOrganizationAsync(current.GetRequired().UserId, ct);
        var row = await db.ChangeRequests.AsNoTracking().SingleOrDefaultAsync(x =>
            x.RequestPublicId == id && x.OrganizationId == org, ct)
            ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
        await tx.CommitAsync(ct);
        return ToView(row);
    }

    private static V180B3RequestView ToView(V180B3ChangeRequest r) => new(
        r.RequestPublicId, r.EntityId, r.TeamId, r.RiskCode, r.Status,
        r.RequestedByUserId, r.SubmittedAt, r.BeforeJson, r.ProposedJson,
        Convert.ToBase64String(r.RowVersion),r.OperationCode);
}
