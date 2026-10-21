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
        var adminReport = path.StartsWith("/api/v1/admin/query-exports/", StringComparison.OrdinalIgnoreCase);
        var tripReport = path.Equals("/api/v1/query/trips/export.xlsx", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/v1/query/trips/export.pdf", StringComparison.OrdinalIgnoreCase);
        if (!adminReport && !tripReport) { await next(); return; }
        ActionExecutedContext? executed = null;
        void Stop(ObjectResult result) { context.Result = result; if (executed is not null) executed.Result = result; }
        try
        {
            var token = current.GetRequired();
            var before = await CheckAsync(token, adminReport, path, context.HttpContext.RequestAborted);
            if (tripReport && token.Roles[0].Equals("leader", StringComparison.OrdinalIgnoreCase))
            {
                if (!DateOnly.TryParse(context.HttpContext.Request.Query["startDate"].FirstOrDefault(), out var start)
                    || start < BusinessTime.Today)
                { context.Result = Denied("REPORT_EXPORT_HISTORICAL_TEAM_POLICY_HOLD"); return; }
            }
            executed = await next();
            if (executed.Exception is not null && !executed.ExceptionHandled)
            {
                executed.ExceptionHandled = true;
                executed.Result = executed.Exception is UnauthorizedAccessException
                    ? Denied("REPORT_EXPORT_LIVE_AUTHORIZATION_DENIED")
                    : new ObjectResult(new { code = "REPORT_EXPORT_FAILED" }) { StatusCode = executed.Exception is InvalidOperationException ? 400 : 503 };
                return;
            }
            var after = await CheckAsync(token, adminReport, path, context.HttpContext.RequestAborted);
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

    private async Task<string> CheckAsync(CurrentUserDto token, bool adminReport, string path, CancellationToken ct)
    {
        if (token.Roles.Count != 1 || !token.OrganizationId.HasValue)
            throw new UnauthorizedAccessException();
        var role = token.Roles[0].ToLowerInvariant();
        if (adminReport ? role != "admin" : role is not ("admin" or "leader" or "visitor" or "supervisor"))
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
        if (role == "leader" && !token.TeamIds.OrderBy(x => x).SequenceEqual(live.TeamIds.OrderBy(x => x)))
            throw new UnauthorizedAccessException();
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
