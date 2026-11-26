using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Api;

// Narrow G1 security delta. Does not enable Package A, HR grants or new columns.
public sealed class G1ReportExportGuard(
    AppDbContext db, IUserRepository users, IV170AccessControl access,
    ICurrentUserService current) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var path = context.HttpContext.Request.Path.Value ?? "";
        // Exact read endpoints only: never widen Admin or trip-report permissions.
        var normalized = path.TrimEnd('/');
        var scopedLocation = HttpMethods.IsGet(context.HttpContext.Request.Method)
            && (normalized.Equals("/api/v1/scoped-location-export/locations", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("/api/v1/scoped-location-export/locations.xlsx", StringComparison.OrdinalIgnoreCase));
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var maintenanceRead = HttpMethods.IsGet(context.HttpContext.Request.Method)
            && parts.Length == 5 && parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("v1", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("locations", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[3], out _)
            && parts[4].Equals("maintenance", StringComparison.OrdinalIgnoreCase);
        var locationRead = scopedLocation || maintenanceRead;
        // Own request history is independent of current team membership; it still requires fresh actor/HR/role authorization before and after the action.
        var ownLocationRequest = HttpMethods.IsGet(context.HttpContext.Request.Method)
            && (normalized.Equals("/api/v1/optionc-candidate/requests/mine/search",StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("/api/v1/optionc-candidate/requests/mine.xlsx",StringComparison.OrdinalIgnoreCase));
        var adminReport = path.StartsWith("/api/v1/admin/query-exports/", StringComparison.OrdinalIgnoreCase);
        var masterData = path.StartsWith("/api/v1/admin/master-data/", StringComparison.OrdinalIgnoreCase);
        var peopleAdmin = path.Equals("/api/v1/admin/people", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/admin/people/", StringComparison.OrdinalIgnoreCase);
        var masterWrite = !HttpMethods.IsGet(context.HttpContext.Request.Method)
            && new[] { "/api/v1/mileage-rate-rules", "/api/v1/projects", "/api/v1/visit-types" }
                .Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
        // Newly enabled admin maintenance and safe-delete permissions require
        // the same fresh account/HR/organization/role check, not stale JWT claims.
        var adminMaintenance = new[] { "/api/v1/admin/projects", "/api/v1/admin/teams",
                "/api/v1/admin/visit-types", "/api/v1/admin/mileage-rate-rules",
                "/api/v1/admin/centers", "/api/v1/admin/deployment-sites", "/api/v1/admin/users" }
            .Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
        // The legacy workbook confirmation is an admin-only command in V160FinalService.
        // Its preview/error download remains available to Leader under existing rules.
        var legacyImport = path.StartsWith("/api/v1/imports/", StringComparison.OrdinalIgnoreCase);
        var legacyAdminImport = legacyImport && (context.HttpContext.User.IsInRole("admin")
            || (HttpMethods.IsPost(context.HttpContext.Request.Method)
                && path.TrimEnd('/').EndsWith("/confirm", StringComparison.OrdinalIgnoreCase)));
        var liveAdmin = masterData || peopleAdmin || masterWrite || adminMaintenance || legacyAdminImport;
        var tripReport = path.Equals("/api/v1/query/trips/export.xlsx", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/v1/query/trips/export.pdf", StringComparison.OrdinalIgnoreCase);
        var correctionReport = HttpMethods.IsGet(context.HttpContext.Request.Method)
            && (normalized.Equals("/api/v1/corrections/export.xlsx", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("/api/v1/corrections/export.pdf", StringComparison.OrdinalIgnoreCase));
        if (!adminReport && !tripReport && !correctionReport && !liveAdmin && !locationRead && !ownLocationRequest) { await next(); return; }
        if (correctionReport) path = normalized;
        // Master-data commands cannot trust a signed but stale organization/role claim.
        // Reuse the live account, HR and role check before any SQL write starts.
        if (liveAdmin)
        {
            try
            {
                await CheckAsync(current.GetRequired(), true, path, context.HttpContext.RequestAborted);
            }
            catch (UnauthorizedAccessException)
            {
                context.Result = Denied("ADMIN_LIVE_AUTHORIZATION_DENIED");
                return;
            }
            catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                context.Result = new ObjectResult(new { code = "ADMIN_AUTHORIZATION_UNAVAILABLE" }) { StatusCode = 503 };
                return;
            }
            await next();
            return;
        }
        // Owner-confirmed company-wide Visit Type master: its existing admin-only
        // export uses the same live account, organization and role checks before and after generation.
        ActionExecutedContext? executed = null;
        void Stop(ObjectResult result) { context.Result = result; if (executed is not null) executed.Result = result; }
        try
        {
            var token = current.GetRequired();
            var before = await CheckAsync(token, adminReport, path, context.HttpContext.RequestAborted, locationRead, scopedLocation);
            // A calendar date is not an authorization boundary.
            // The export service invokes the same server-side scoped trip
            // repository as the query page. Its current-team / approved
            // snapshot checks are applied to *each* row, including history.
            // Keep before/after live-role and revocation checks below.
            executed = await next();
            if (executed.Exception is not null && !executed.ExceptionHandled)
            {
                executed.ExceptionHandled = true;
                executed.Result = executed.Exception is UnauthorizedAccessException
                    ? Denied("REPORT_EXPORT_LIVE_AUTHORIZATION_DENIED")
                    : new ObjectResult(new { code = "REPORT_EXPORT_FAILED" }) { StatusCode = executed.Exception is InvalidOperationException ? 400 : 503 };
                return;
            }
            var after = await CheckAsync(token, adminReport, path, context.HttpContext.RequestAborted, locationRead, scopedLocation);
            if (before != after) executed.Result = Denied("REPORT_EXPORT_SCOPE_CHANGED");
        }
        catch (UnauthorizedAccessException)
        { Stop(Denied("REPORT_EXPORT_LIVE_AUTHORIZATION_DENIED")); }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Do not expose database failures, identities or filters through this guard.
            Stop(new ObjectResult(new { code = "REPORT_EXPORT_VERIFICATION_UNAVAILABLE" }) { StatusCode = 503 });
        }
    }

    private async Task<string> CheckAsync(CurrentUserDto token, bool adminReport, string path, CancellationToken ct,
        bool locationRead = false, bool scopedLocation = false)
    {
        if (token.Roles.Count != 1 || !token.OrganizationId.HasValue)
            throw new UnauthorizedAccessException();
        var role = token.Roles[0].ToLowerInvariant();
        if (adminReport ? role != "admin" : role is not ("admin" or "leader" or "visitor" or "supervisor"))
            throw new UnauthorizedAccessException();
        if (scopedLocation && role is not ("visitor" or "leader")
            || locationRead && role is not ("admin" or "visitor" or "leader"))
            throw new UnauthorizedAccessException();
        var account = await db.Users.AsNoTracking().Where(x => x.UserId == token.UserId)
            .Select(x => new { x.IsActive, x.OrganizationId }).SingleOrDefaultAsync(ct);
        if (account is null || !account.IsActive || account.OrganizationId != token.OrganizationId
            || !(await access.EvaluateLoginAsync(token.UserId, account.IsActive, ct)).IsAllowed)
            throw new UnauthorizedAccessException();
        var live = await users.GetProfileAsync(token.UserId, ct);
        if (live is null || live.OrganizationId != token.OrganizationId
            || !live.Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException();
        if ((role == "leader" || locationRead && role == "visitor")
            && !token.TeamIds.OrderBy(x => x).SequenceEqual(live.TeamIds.OrderBy(x => x)))
            throw new UnauthorizedAccessException();
        if (locationRead && role != "admin")
        {
            // Location notes use the JWT team IDs internally. Do not let a
            // stale token regain a removed team through a shared Location.
            // Check effective assignments and the active scope projection,
            // not merely the legacy primary-team fallback in GetProfileAsync.
            var today = BusinessTime.Today;
            var teams = await (from a in db.UserTeamAssignments.AsNoTracking()
                join t in db.Teams.AsNoTracking() on a.TeamId equals t.TeamId
                where a.UserId == token.UserId && t.OrganizationId == token.OrganizationId
                    && t.IsActive && (!t.EffectiveFrom.HasValue || t.EffectiveFrom <= today)
                    && (!t.EffectiveTo.HasValue || t.EffectiveTo >= today)
                    && a.EffectiveFrom <= today && (!a.EffectiveTo.HasValue || a.EffectiveTo >= today)
                    && db.UserTeamScopes.Any(p => p.UserId == token.UserId && p.TeamId == t.TeamId && p.IsActive)
                select t.TeamId).Distinct().OrderBy(x => x).ToArrayAsync(ct);
            if (teams.Length == 0 || !token.TeamIds.OrderBy(x => x).SequenceEqual(teams))
                throw new UnauthorizedAccessException();
        }
        if (role == "supervisor")
            await access.EnsureExportAllowedAsync(live with { Roles = [role] }, path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "xlsx", ct);
        return JsonSerializer.Serialize(new {
            live.OrganizationId, Role = role, Teams = live.TeamIds.OrderBy(x => x),
            Scopes = (live.DataScopes ?? []).Select(x => new { x.ScopeType, x.OrganizationId, x.TeamId })
                .OrderBy(x => x.ScopeType).ThenBy(x => x.OrganizationId).ThenBy(x => x.TeamId)
        });
    }

    private static ObjectResult Denied(string code) => new(new { code }) { StatusCode = 403 };
}
