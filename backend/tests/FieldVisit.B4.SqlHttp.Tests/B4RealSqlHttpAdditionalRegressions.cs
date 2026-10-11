using System.Net;
using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.B4.SqlHttp.Tests;

/// <summary>
/// Additional REAL AppJwt + HTTP + disposable localhost SQL Server concurrency/rollback cases.
/// Deliberately uses the existing random database fixture; no migrations or UAT resources.
/// </summary>
public sealed partial class B4RealSqlHttpPipelineTests
{
    [Fact]
    public async Task SQL_HTTP_SUBMIT_two_concurrent_clients_commit_exactly_one_snapshot()
    {
        await using var f = await DisposableTestDatabase.StartAsync();
        using var host = new RealSqlHttpFactory(f.Connection);
        using var a = await LoginAsync(host, f);
        using var b = await LoginAsync(host, f);
        var created = await CreateReadyDraftAsync(a, f);

        var pendingA = SubmitDraftAsync(a, created.Id, created.Version);
        var pendingB = SubmitDraftAsync(b, created.Id, created.Version);
        using var responseA = await pendingA;
        using var responseB = await pendingB;
        var statuses = new[] { responseA.StatusCode, responseB.StatusCode };
        Assert.Single(statuses, x => x == HttpStatusCode.OK);
        Assert.Single(statuses, x => x is HttpStatusCode.Conflict
            or HttpStatusCode.UnprocessableEntity);

        var state = await DraftStateAsync(f, created.Id);
        Assert.Equal("Submitted", state.Status);
        Assert.Equal(2, state.AuditCount);   // create + one committed submit
        Assert.Equal(2, state.HistoryCount); // create + one committed submit
        await using var db = f.Db();
        var snapshot = Assert.Single(await db.VisitTripSnapshots.AsNoTracking()
            .Where(x => x.VisitTripId == created.Id).ToListAsync());
        Assert.Equal(1, snapshot.SnapshotVersion);
        Assert.Single(await db.VisitTripSnapshotStops.AsNoTracking()
            .Where(x => x.VisitTripSnapshotId == snapshot.VisitTripSnapshotId)
            .ToListAsync());
    }

    [Fact]
    public async Task SQL_HTTP_SUBMIT_racing_draft_update_never_commits_both_versions()
    {
        await using var f = await DisposableTestDatabase.StartAsync();
        using var host = new RealSqlHttpFactory(f.Connection);
        using var submitter = await LoginAsync(host, f);
        using var editor = await LoginAsync(host, f);
        var created = await CreateReadyDraftAsync(submitter, f);

        var submit = SubmitDraftAsync(submitter, created.Id, created.Version);
        var edit = UpdateDraftAsync(editor, created.Id, created.Version,
            ReadyForSubmit(f) with { Notes = "Concurrent draft update" });
        using var submitResponse = await submit;
        using var editResponse = await edit;
        var statuses = new[] { submitResponse.StatusCode, editResponse.StatusCode };
        Assert.Single(statuses, x => x == HttpStatusCode.OK);
        Assert.Single(statuses, x => x is HttpStatusCode.Conflict
            or HttpStatusCode.UnprocessableEntity);

        var state = await DraftStateAsync(f, created.Id);
        Assert.Equal(2, state.AuditCount);
        Assert.Equal(2, state.HistoryCount);
        await using var db = f.Db();
        var snaps = await db.VisitTripSnapshots.AsNoTracking()
            .Where(x => x.VisitTripId == created.Id).ToListAsync();
        if (submitResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal("Submitted", state.Status);
            Assert.Equal(1, Assert.Single(snaps).SnapshotVersion);
        }
        else
        {
            Assert.Equal("Draft", state.Status);
            Assert.Equal("Concurrent draft update", state.Notes);
            Assert.Empty(snaps);
        }
    }

    [Fact]
    public async Task SQL_HTTP_SUBMIT_injected_snapshot_SQL_error_rolls_back_history_and_audit()
    {
        await using var f = await DisposableTestDatabase.StartAsync();
        using var host = new RealSqlHttpFactory(f.Connection);
        using var client = await LoginAsync(host, f);
        var created = await CreateReadyDraftAsync(client, f);
        var before = await DraftStateAsync(f, created.Id);

        await using (var db = f.Db())
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER dbo.TR_B4_Isolated_RejectSnapshot " +
                "ON dbo.VisitTripSnapshots AFTER INSERT AS " +
                "BEGIN SET NOCOUNT ON; " +
                "THROW 51001, N'ISOLATED_SNAPSHOT_WRITE_FAILURE_MARKER', 1; END");
        }

        using var response = await SubmitDraftAsync(client, created.Id, created.Version);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ISOLATED_SNAPSHOT_WRITE_FAILURE_MARKER",
            body, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlException", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await DraftStateAsync(f, created.Id));
        await using var check = f.Db();
        Assert.Empty(await check.VisitTripSnapshots.AsNoTracking().ToListAsync());
        Assert.Empty(await check.VisitTripSnapshotStops.AsNoTracking().ToListAsync());
    }
}
