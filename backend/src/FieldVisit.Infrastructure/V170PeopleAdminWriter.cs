using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// v1.7 People/Access write model.
///
/// External supervisors:
/// - have no EmployeeNo
/// - are never Team Members
/// - receive read visibility only through UserDataScopes
/// - receive export permission only through UserCapabilities
/// </summary>
public sealed class V170PeopleAdminWriter(
    AppDbContext db)
    : IV170PeopleAdminWriter
{
    public async Task<int> CreateExternalSupervisorAsync(
        CurrentUserDto admin,
        SaveExternalSupervisorRequest request,
        CancellationToken ct)
    {
        request =
            V170ExternalSupervisorRules.Normalize(
                request);

        var orgId =
            admin.OrganizationId
            ?? throw new InvalidOperationException(
                "目前管理者缺少 OrganizationId。");

        var duplicateEmail =
            await db.Users
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Email != null
                        && x.Email.ToLower()
                           == request.Email.ToLower(),
                    ct);

        if (duplicateEmail)
        {
            throw new InvalidOperationException(
                "此 Email 已存在於系統中。");
        }

        if (request.ScopeType
            == DataScopeTypes.Team)
        {
            var validTeamIds =
                await db.Teams
                    .AsNoTracking()
                    .Where(
                        x =>
                            request.TeamIds.Contains(
                                x.TeamId)
                            && x.OrganizationId == orgId
                            && x.IsActive)
                    .Select(x => x.TeamId)
                    .ToListAsync(ct);

            if (validTeamIds.Count
                != request.TeamIds.Count)
            {
                throw new InvalidOperationException(
                    "包含不存在、已停用或不屬於目前 Organization 的 Team。");
            }
        }

        var role =
            await db.Roles
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync(ct);

        var supervisorRole =
            role.FirstOrDefault(
                x =>
                    x.RoleCode.Equals(
                        "supervisor",
                        StringComparison.OrdinalIgnoreCase)
                    || x.RoleCode.Equals(
                        "government",
                        StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "找不到 Supervisor Role。");

        var strategy =
            db.Database.CreateExecutionStrategy();

        var createdUserId = 0;

        await strategy.ExecuteAsync(
            async () =>
            {
                db.ChangeTracker.Clear();

                await using var tx =
                    await db.Database
                        .BeginTransactionAsync(ct);

                var now =
                    DateTime.UtcNow;

                var user =
                    new User
                    {
                        OrganizationId = orgId,
                        TeamId = null,

                        // External identity deliberately has
                        // no employee number.
                        EmployeeNo = null,

                        DisplayName =
                            request.DisplayName,

                        Email =
                            request.Email,

                        EntraObjectId = null,

                        IsActive =
                            request.AdminEnabled,

                        CreatedAt = now
                    };

                await db.Users.AddAsync(
                    user,
                    ct);

                await db.SaveChangesAsync(ct);

                createdUserId =
                    user.UserId;

                var userCode =
                    await NewExternalUserCodeAsync(
                        ct);

                await EnsureIdentityBindingAvailableAsync(
                    request.IdentityProvider,
                    request.EntraTenantId,
                    request.EntraObjectId,
                    user.UserId,
                    ct);

                await db.UserIdentityProfiles
                    .AddAsync(
                        new UserIdentityProfile
                        {
                            UserId =
                                user.UserId,

                            UserType =
                                UserTypes.External,

                            UserCode =
                                userCode,

                            IdentityProvider =
                                request.IdentityProvider
                                ?? "Demo",

                            EntraTenantId =
                                request.EntraTenantId,

                            EntraObjectId =
                                request.EntraObjectId,

                            ExternalOrganization =
                                request.ExternalOrganization,

                            ExternalTitle =
                                request.ExternalTitle,

                            AuthorizationFrom =
                                request.AuthorizationFrom,

                            AuthorizationTo =
                                request.AuthorizationTo,

                            CreatedAt =
                                now
                        },
                        ct);

                // Effective-dated source of truth.
                await db.UserRoleAssignments
                    .AddAsync(
                        new UserRoleAssignment
                        {
                            UserId =
                                user.UserId,

                            RoleId =
                                supervisorRole.RoleId,

                            EffectiveFrom =
                                request.AuthorizationFrom,

                            EffectiveTo =
                                request.AuthorizationTo,

                            AssignedByUserId =
                                admin.UserId,

                            CreatedAt =
                                now
                        },
                        ct);

                // v1.6 compatibility projection.
                await db.UserRoles.AddAsync(
                    new UserRole
                    {
                        UserId =
                            user.UserId,

                        RoleId =
                            supervisorRole.RoleId,

                        AssignedAt =
                            now
                    },
                    ct);

                if (request.ScopeType
                    == DataScopeTypes.Organization)
                {
                    await db.UserDataScopes
                        .AddAsync(
                            new UserDataScope
                            {
                                UserId =
                                    user.UserId,

                                ScopeType =
                                    DataScopeTypes.Organization,

                                OrganizationId =
                                    orgId,

                                TeamId =
                                    null,

                                EffectiveFrom =
                                    request.AuthorizationFrom,

                                EffectiveTo =
                                    request.AuthorizationTo,

                                GrantedByUserId =
                                    admin.UserId,

                                CreatedAt =
                                    now
                            },
                            ct);
                }
                else
                {
                    foreach (var teamId
                             in request.TeamIds)
                    {
                        await db.UserDataScopes
                            .AddAsync(
                                new UserDataScope
                                {
                                    UserId =
                                        user.UserId,

                                    ScopeType =
                                        DataScopeTypes.Team,

                                    OrganizationId =
                                        null,

                                    TeamId =
                                        teamId,

                                    EffectiveFrom =
                                        request.AuthorizationFrom,

                                    EffectiveTo =
                                        request.AuthorizationTo,

                                    GrantedByUserId =
                                        admin.UserId,

                                    CreatedAt =
                                        now
                                },
                                ct);
                    }
                }

                await AddCapabilityAsync(
                    user.UserId,
                    CapabilityCodes.ExportExcel,
                    request.CanExportExcel,
                    request.AuthorizationFrom,
                    request.AuthorizationTo,
                    admin.UserId,
                    now,
                    ct);

                await AddCapabilityAsync(
                    user.UserId,
                    CapabilityCodes.ExportPdf,
                    request.CanExportPdf,
                    request.AuthorizationFrom,
                    request.AuthorizationTo,
                    admin.UserId,
                    now,
                    ct);

                await db.AuditLogs.AddAsync(
                    new AuditLog
                    {
                        UserId =
                            admin.UserId,

                        EntityType =
                            "User",

                        EntityId =
                            user.UserId.ToString(),

                        Action =
                            "ExternalSupervisorCreate",

                        NewValues =
                            JsonSerializer.Serialize(
                                new
                                {
                                    UserCode =
                                        userCode,

                                    request.DisplayName,
                                    request.Email,
                                    request.ExternalOrganization,
                                    request.ExternalTitle,
                                    request.AuthorizationFrom,
                                    request.AuthorizationTo,
                                    request.ScopeType,
                                    request.TeamIds,
                                    request.CanExportExcel,
                                    request.CanExportPdf,
                                    request.AdminEnabled,
                                    request.IdentityProvider,
                                    request.EntraTenantId,
                                    request.EntraObjectId
                                }),

                        CreatedAt =
                            now
                    },
                    ct);

                await db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
            });

        return createdUserId;
    }

    public async Task UpdateExternalSupervisorAsync(
        CurrentUserDto admin,
        int userId,
        UpdateExternalSupervisorRequest request,
        CancellationToken ct)
    {
        var today =
            BusinessTime.Today;

        request =
            V170ExternalSupervisorUpdateRules.Normalize(
                request,
                today);

        var orgId =
            admin.OrganizationId
            ?? throw new InvalidOperationException(
                "目前管理者缺少 OrganizationId。");

        var strategy =
            db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(
            async () =>
            {
                db.ChangeTracker.Clear();

                await using var tx =
                    await db.Database
                        .BeginTransactionAsync(ct);

                var now =
                    DateTime.UtcNow;

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == userId
                                && x.OrganizationId == orgId,
                            ct)
                    ?? throw new KeyNotFoundException(
                        "找不到外部督導。");

                var identity =
                    await db.UserIdentityProfiles
                        .FirstOrDefaultAsync(
                            x => x.UserId == userId,
                            ct)
                    ?? throw new InvalidOperationException(
                        "此帳號缺少 v1.7 Identity Profile。");

                if (!identity.UserType.Equals(
                        UserTypes.External,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "只有 External User 可以使用此外部督導異動功能。");
                }

                await EnsureIdentityBindingAvailableAsync(
                    request.IdentityProvider,
                    request.EntraTenantId,
                    request.EntraObjectId,
                    userId,
                    ct);

                var duplicateEmail =
                    await db.Users
                        .AsNoTracking()
                        .AnyAsync(
                            x =>
                                x.UserId != userId
                                && x.Email != null
                                && x.Email.ToLower()
                                   == request.Email.ToLower(),
                            ct);

                if (duplicateEmail)
                {
                    throw new InvalidOperationException(
                        "此 Email 已存在於系統中。");
                }

                if (request.ScopeType
                    == DataScopeTypes.Team)
                {
                    var validTeamIds =
                        await db.Teams
                            .AsNoTracking()
                            .Where(
                                x =>
                                    request.TeamIds.Contains(
                                        x.TeamId)
                                    && x.OrganizationId == orgId
                                    && x.IsActive)
                            .Select(x => x.TeamId)
                            .ToListAsync(ct);

                    if (validTeamIds.Count
                        != request.TeamIds.Count)
                    {
                        throw new InvalidOperationException(
                            "包含不存在、已停用或不屬於目前 Organization 的 Team。");
                    }
                }

                if (!request.ConfirmRetroactive)
                {
                    var authorizationStartChangedToPast =
                        identity.AuthorizationFrom
                            != request.AuthorizationFrom
                        && request.AuthorizationFrom < today;

                    var authorizationEndChangedToPast =
                        identity.AuthorizationTo
                            != request.AuthorizationTo
                        && request.AuthorizationTo < today;

                    if (authorizationStartChangedToPast
                        || authorizationEndChangedToPast)
                    {
                        throw new InvalidOperationException(
                            "授權日期異動涉及歷史期間，請二次確認回溯異動。");
                    }
                }

                var supervisorRole =
                    await db.Roles
                        .AsNoTracking()
                        .Where(x => x.IsActive)
                        .FirstOrDefaultAsync(
                            x =>
                                x.RoleCode.ToLower()
                                    == "supervisor"
                                || x.RoleCode.ToLower()
                                    == "government",
                            ct)
                    ?? throw new InvalidOperationException(
                        "找不到 Supervisor Role。");

                var roleAssignments =
                    await db.UserRoleAssignments
                        .Where(
                            x =>
                                x.UserId == userId
                                && x.RoleId
                                   == supervisorRole.RoleId)
                        .ToListAsync(ct);

                if (roleAssignments.Count != 1)
                {
                    throw new InvalidOperationException(
                        "External Supervisor Role Assignment 資料不完整，請由 IT 檢查。");
                }

                var scopesBefore =
                    await db.UserDataScopes
                        .AsNoTracking()
                        .Where(x => x.UserId == userId)
                        .OrderBy(x => x.EffectiveFrom)
                        .Select(
                            x => new
                            {
                                x.ScopeType,
                                x.OrganizationId,
                                x.TeamId,
                                x.EffectiveFrom,
                                x.EffectiveTo
                            })
                        .ToListAsync(ct);

                var capabilitiesBefore =
                    await db.UserCapabilities
                        .AsNoTracking()
                        .Where(x => x.UserId == userId)
                        .OrderBy(x => x.CapabilityCode)
                        .ThenBy(x => x.EffectiveFrom)
                        .Select(
                            x => new
                            {
                                x.CapabilityCode,
                                x.IsAllowed,
                                x.EffectiveFrom,
                                x.EffectiveTo
                            })
                        .ToListAsync(ct);

                var oldValues =
                    new
                    {
                        user.DisplayName,
                        user.Email,
                        user.IsActive,

                        identity.ExternalOrganization,
                        identity.ExternalTitle,
                        identity.AuthorizationFrom,
                        identity.AuthorizationTo,

                        Identity =
                            new
                            {
                                identity.IdentityProvider,
                                identity.EntraTenantId,
                                identity.EntraObjectId
                            },

                        Scopes =
                            scopesBefore,

                        Capabilities =
                            capabilitiesBefore
                    };

                user.DisplayName =
                    request.DisplayName;

                user.Email =
                    request.Email;

                user.IsActive =
                    request.AdminEnabled;

                user.UpdatedAt =
                    now;

                identity.ExternalOrganization =
                    request.ExternalOrganization;

                identity.ExternalTitle =
                    request.ExternalTitle;

                identity.AuthorizationFrom =
                    request.AuthorizationFrom;

                identity.AuthorizationTo =
                    request.AuthorizationTo;

                ApplyIdentityBinding(
                    identity,
                    request.IdentityProvider,
                    request.EntraTenantId,
                    request.EntraObjectId,
                    now);

                identity.UpdatedAt =
                    now;

                var roleAssignment =
                    roleAssignments.Single();

                roleAssignment.EffectiveFrom =
                    request.AuthorizationFrom;

                roleAssignment.EffectiveTo =
                    request.AuthorizationTo;

                await PrepareScopeVersionAsync(
                    userId,
                    request.ChangeEffectiveFrom,
                    ct);

                await PrepareCapabilityVersionAsync(
                    userId,
                    CapabilityCodes.ExportExcel,
                    request.ChangeEffectiveFrom,
                    ct);

                await PrepareCapabilityVersionAsync(
                    userId,
                    CapabilityCodes.ExportPdf,
                    request.ChangeEffectiveFrom,
                    ct);

                if (request.ScopeType
                    == DataScopeTypes.Organization)
                {
                    await db.UserDataScopes
                        .AddAsync(
                            new UserDataScope
                            {
                                UserId =
                                    userId,

                                ScopeType =
                                    DataScopeTypes.Organization,

                                OrganizationId =
                                    orgId,

                                TeamId =
                                    null,

                                EffectiveFrom =
                                    request.ChangeEffectiveFrom,

                                EffectiveTo =
                                    request.AuthorizationTo,

                                GrantedByUserId =
                                    admin.UserId,

                                CreatedAt =
                                    now
                            },
                            ct);
                }
                else
                {
                    foreach (var teamId
                             in request.TeamIds)
                    {
                        await db.UserDataScopes
                            .AddAsync(
                                new UserDataScope
                                {
                                    UserId =
                                        userId,

                                    ScopeType =
                                        DataScopeTypes.Team,

                                    OrganizationId =
                                        null,

                                    TeamId =
                                        teamId,

                                    EffectiveFrom =
                                        request.ChangeEffectiveFrom,

                                    EffectiveTo =
                                        request.AuthorizationTo,

                                    GrantedByUserId =
                                        admin.UserId,

                                    CreatedAt =
                                        now
                                },
                                ct);
                    }
                }

                await AddCapabilityAsync(
                    userId,
                    CapabilityCodes.ExportExcel,
                    request.CanExportExcel,
                    request.ChangeEffectiveFrom,
                    request.AuthorizationTo,
                    admin.UserId,
                    now,
                    ct);

                await AddCapabilityAsync(
                    userId,
                    CapabilityCodes.ExportPdf,
                    request.CanExportPdf,
                    request.ChangeEffectiveFrom,
                    request.AuthorizationTo,
                    admin.UserId,
                    now,
                    ct);

                await db.AuditLogs.AddAsync(
                    new AuditLog
                    {
                        UserId =
                            admin.UserId,

                        EntityType =
                            "User",

                        EntityId =
                            userId.ToString(),

                        Action =
                            request.AdminEnabled
                                ? "ExternalSupervisorUpdate"
                                : "ExternalSupervisorDisable",

                        NewValues =
                            JsonSerializer.Serialize(
                                new
                                {
                                    Old =
                                        oldValues,

                                    New =
                                        new
                                        {
                                            request.DisplayName,
                                            request.Email,
                                            request.ExternalOrganization,
                                            request.ExternalTitle,
                                            request.AuthorizationFrom,
                                            request.AuthorizationTo,
                                            request.ScopeType,
                                            request.TeamIds,
                                            request.CanExportExcel,
                                            request.CanExportPdf,
                                            request.AdminEnabled,
                                            request.ChangeEffectiveFrom,
                                            request.IdentityProvider,
                                            request.EntraTenantId,
                                            request.EntraObjectId
                                        },

                                    request.ConfirmRetroactive
                                }),

                        CreatedAt =
                            now
                    },
                    ct);

                await db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
            });
    }

    public async Task UpdateInternalUserAccessAsync(
        CurrentUserDto admin,
        int userId,
        UpdateInternalUserAccessRequest request,
        CancellationToken ct)
    {
        var today =
            BusinessTime.Today;

        request =
            V170InternalUserAccessRules.Normalize(
                request,
                today);

        var orgId =
            admin.OrganizationId
            ?? throw new InvalidOperationException(
                "目前管理者缺少 OrganizationId。");

        var strategy =
            db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(
            async () =>
            {
                db.ChangeTracker.Clear();

                await using var tx =
                    await db.Database
                        .BeginTransactionAsync(ct);

                var now =
                    DateTime.UtcNow;

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == userId
                                && x.OrganizationId == orgId,
                            ct)
                    ?? throw new KeyNotFoundException(
                        "找不到 Internal User。");

                var identity =
                    await db.UserIdentityProfiles
                        .FirstOrDefaultAsync(
                            x => x.UserId == userId,
                            ct)
                    ?? throw new InvalidOperationException(
                        "此帳號缺少 v1.7 Identity Profile。");

                if (!identity.UserType.Equals(
                        UserTypes.Internal,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "External User 不可使用 Internal User 權限異動功能。");
                }

                await EnsureIdentityBindingAvailableAsync(
                    request.IdentityProvider,
                    request.EntraTenantId,
                    request.EntraObjectId,
                    userId,
                    ct);

                var requestedTeamIds =
                    request.TeamAssignments
                        .Select(x => x.TeamId)
                        .ToList();

                if (requestedTeamIds.Count > 0)
                {
                    var validTeamIds =
                        await db.Teams
                            .AsNoTracking()
                            .Where(
                                x =>
                                    requestedTeamIds.Contains(
                                        x.TeamId)
                                    && x.OrganizationId == orgId
                                    && x.IsActive)
                            .Select(x => x.TeamId)
                            .ToListAsync(ct);

                    if (validTeamIds.Count
                        != requestedTeamIds.Count)
                    {
                        throw new InvalidOperationException(
                            "包含不存在、已停用或不屬於目前 Organization 的 Team。");
                    }
                }

                var roleRows =
                    await db.Roles
                        .AsNoTracking()
                        .Where(x => x.IsActive)
                        .ToListAsync(ct);

                var requestedRoles =
                    request.Roles
                        .ToHashSet(
                            StringComparer.OrdinalIgnoreCase);

                var targetRoles =
                    roleRows
                        .Where(
                            x =>
                                requestedRoles.Contains(
                                    NormalizeRoleCode(
                                        x.RoleCode)))
                        .ToList();

                if (targetRoles
                        .Select(
                            x =>
                                NormalizeRoleCode(
                                    x.RoleCode))
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .Count()
                    != requestedRoles.Count)
                {
                    throw new InvalidOperationException(
                        "找不到一個或多個指定 Role。");
                }

                var rolesBefore =
                    await (
                        from assignment
                            in db.UserRoleAssignments
                                .AsNoTracking()
                        join role
                            in db.Roles.AsNoTracking()
                            on assignment.RoleId
                            equals role.RoleId
                        where assignment.UserId
                              == userId
                        orderby
                            assignment.EffectiveFrom,
                            role.RoleCode
                        select new
                        {
                            Role =
                                role.RoleCode,

                            assignment.EffectiveFrom,
                            assignment.EffectiveTo
                        })
                        .ToListAsync(ct);

                var teamsBefore =
                    await (
                        from assignment
                            in db.UserTeamAssignments
                                .AsNoTracking()
                        join team
                            in db.Teams.AsNoTracking()
                            on assignment.TeamId
                            equals team.TeamId
                        where assignment.UserId
                              == userId
                        orderby
                            assignment.EffectiveFrom,
                            team.TeamCode
                        select new
                        {
                            team.TeamId,
                            team.TeamCode,
                            team.TeamName,
                            assignment.IsPrimary,
                            assignment.EffectiveFrom,
                            assignment.EffectiveTo
                        })
                        .ToListAsync(ct);

                var oldValues =
                    new
                    {
                        Identity =
                            new
                            {
                                identity.IdentityProvider,
                                identity.EntraTenantId,
                                identity.EntraObjectId
                            },

                        Roles = rolesBefore,
                        Teams = teamsBefore
                    };

                /*
                 * Internal login eligibility is derived from effective HR status.
                 * This legacy writer may update roles, teams and identity binding,
                 * but must never toggle Users.IsActive for an Internal user.
                 */
                ApplyIdentityBinding(
                    identity,
                    request.IdentityProvider,
                    request.EntraTenantId,
                    request.EntraObjectId,
                    now);

                await PrepareInternalRoleVersionsAsync(
                    userId,
                    request.ChangeEffectiveFrom,
                    ct);

                await PrepareInternalTeamVersionsAsync(
                    userId,
                    request.ChangeEffectiveFrom,
                    ct);

                foreach (var role
                         in targetRoles)
                {
                    await db.UserRoleAssignments
                        .AddAsync(
                            new UserRoleAssignment
                            {
                                UserId =
                                    userId,

                                RoleId =
                                    role.RoleId,

                                EffectiveFrom =
                                    request.ChangeEffectiveFrom,

                                EffectiveTo =
                                    null,

                                AssignedByUserId =
                                    admin.UserId,

                                CreatedAt =
                                    now
                            },
                            ct);
                }

                foreach (var team
                         in request.TeamAssignments)
                {
                    await db.UserTeamAssignments
                        .AddAsync(
                            new UserTeamAssignment
                            {
                                UserId =
                                    userId,

                                TeamId =
                                    team.TeamId,

                                IsPrimary =
                                    team.IsPrimary,

                                EffectiveFrom =
                                    request.ChangeEffectiveFrom,

                                EffectiveTo =
                                    null,

                                AssignedByUserId =
                                    admin.UserId,

                                CreatedAt =
                                    now
                            },
                            ct);
                }

                await db.SaveChangesAsync(ct);

                /*
                 * Keep v1.6 tables as CURRENT-STATE compatibility
                 * projections. Runtime authentication for v1.7 users
                 * reads the effective-dated source of truth directly.
                 */
                await SyncLegacyCurrentProjectionAsync(
                    user,
                    today,
                    admin.UserId,
                    now,
                    ct);

                await db.AuditLogs.AddAsync(
                    new AuditLog
                    {
                        UserId =
                            admin.UserId,

                        EntityType =
                            "User",

                        EntityId =
                            userId.ToString(),

                        Action =
                            "InternalUserAccessUpdate",

                        NewValues =
                            JsonSerializer.Serialize(
                                new
                                {
                                    Old =
                                        oldValues,

                                    New =
                                        new
                                        {
                                            request.Roles,
                                            request.TeamAssignments,
                                            request.ChangeEffectiveFrom,
                                            request.IdentityProvider,
                                            request.EntraTenantId,
                                            request.EntraObjectId
                                        },

                                    request.ConfirmRetroactive
                                }),

                        CreatedAt =
                            now
                    },
                    ct);

                await db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
            });
    }

    private async Task PrepareInternalRoleVersionsAsync(
        int userId,
        DateOnly effectiveFrom,
        CancellationToken ct)
    {
        var rows =
            await db.UserRoleAssignments
                .Where(x => x.UserId == userId)
                .ToListAsync(ct);

        if (rows.Any(
                x => x.EffectiveFrom > effectiveFrom))
        {
            throw new InvalidOperationException(
                "此人員已有較晚生效的 Role 排程，請先處理該排程後再異動。");
        }

        var previousDay =
            PreviousDay(effectiveFrom);

        foreach (var row
                 in rows.Where(
                     x =>
                         x.EffectiveFrom
                             < effectiveFrom
                         && (!x.EffectiveTo.HasValue
                             || x.EffectiveTo
                                >= effectiveFrom)))
        {
            row.EffectiveTo =
                previousDay;
        }

        var sameStart =
            rows.Where(
                    x =>
                        x.EffectiveFrom
                        == effectiveFrom)
                .ToList();

        if (sameStart.Count > 0)
        {
            db.UserRoleAssignments
                .RemoveRange(sameStart);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task PrepareInternalTeamVersionsAsync(
        int userId,
        DateOnly effectiveFrom,
        CancellationToken ct)
    {
        var rows =
            await db.UserTeamAssignments
                .Where(x => x.UserId == userId)
                .ToListAsync(ct);

        if (rows.Any(
                x => x.EffectiveFrom > effectiveFrom))
        {
            throw new InvalidOperationException(
                "此人員已有較晚生效的 Team 排程，請先處理該排程後再異動。");
        }

        var previousDay =
            PreviousDay(effectiveFrom);

        foreach (var row
                 in rows.Where(
                     x =>
                         x.EffectiveFrom
                             < effectiveFrom
                         && (!x.EffectiveTo.HasValue
                             || x.EffectiveTo
                                >= effectiveFrom)))
        {
            row.EffectiveTo =
                previousDay;
        }

        var sameStart =
            rows.Where(
                    x =>
                        x.EffectiveFrom
                        == effectiveFrom)
                .ToList();

        if (sameStart.Count > 0)
        {
            db.UserTeamAssignments
                .RemoveRange(sameStart);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SyncLegacyCurrentProjectionAsync(
        User user,
        DateOnly today,
        int assignedByUserId,
        DateTime now,
        CancellationToken ct)
    {
        var currentRoleIds =
            await db.UserRoleAssignments
                .AsNoTracking()
                .Where(
                    x =>
                        x.UserId == user.UserId
                        && x.EffectiveFrom <= today
                        && (!x.EffectiveTo.HasValue
                            || x.EffectiveTo >= today))
                .Select(x => x.RoleId)
                .Distinct()
                .ToListAsync(ct);

        var legacyRoles =
            await db.UserRoles
                .Where(
                    x => x.UserId == user.UserId)
                .ToListAsync(ct);

        db.UserRoles.RemoveRange(
            legacyRoles.Where(
                x =>
                    !currentRoleIds.Contains(
                        x.RoleId)));

        foreach (var roleId
                 in currentRoleIds.Where(
                     roleId =>
                         legacyRoles.All(
                             x =>
                                 x.RoleId
                                 != roleId)))
        {
            await db.UserRoles.AddAsync(
                new UserRole
                {
                    UserId =
                        user.UserId,

                    RoleId =
                        roleId,

                    AssignedAt =
                        now
                },
                ct);
        }

        var currentTeams =
            await db.UserTeamAssignments
                .AsNoTracking()
                .Where(
                    x =>
                        x.UserId == user.UserId
                        && x.EffectiveFrom <= today
                        && (!x.EffectiveTo.HasValue
                            || x.EffectiveTo >= today))
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.TeamId)
                .ToListAsync(ct);

        if (currentTeams.Count > 0
            && currentTeams.Count(
                x => x.IsPrimary) != 1)
        {
            throw new InvalidOperationException(
                "目前有效 Team Membership 的 Primary Team 資料不正確。");
        }

        var legacyScopes =
            await db.UserTeamScopes
                .Where(
                    x => x.UserId == user.UserId)
                .ToListAsync(ct);

        /*
         * Clear primary first so SQL Server's filtered unique
         * index is never dependent on UPDATE ordering.
         */
        foreach (var scope
                 in legacyScopes.Where(
                     x =>
                         x.IsActive
                         && x.IsPrimary))
        {
            scope.IsPrimary =
                false;
        }

        await db.SaveChangesAsync(ct);

        foreach (var scope
                 in legacyScopes)
        {
            var desired =
                currentTeams.FirstOrDefault(
                    x =>
                        x.TeamId
                        == scope.TeamId);

            scope.IsActive =
                desired is not null;

            scope.IsPrimary =
                desired?.IsPrimary
                == true;

            scope.EndedAt =
                desired is null
                    ? now
                    : null;

            if (desired is not null)
            {
                scope.AssignedAt =
                    now;

                scope.AssignedByUserId =
                    assignedByUserId;
            }
        }

        foreach (var desired
                 in currentTeams.Where(
                     desired =>
                         legacyScopes.All(
                             x =>
                                 x.TeamId
                                 != desired.TeamId)))
        {
            await db.UserTeamScopes.AddAsync(
                new UserTeamScope
                {
                    UserId =
                        user.UserId,

                    TeamId =
                        desired.TeamId,

                    IsPrimary =
                        desired.IsPrimary,

                    IsActive =
                        true,

                    AssignedAt =
                        now,

                    AssignedByUserId =
                        assignedByUserId,

                    EndedAt =
                        null
                },
                ct);
        }

        user.TeamId =
            currentTeams
                .FirstOrDefault(
                    x => x.IsPrimary)
                ?.TeamId;

        user.UpdatedAt =
            now;

        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateInternalEmploymentAsync(
        CurrentUserDto admin,
        int userId,
        UpdateInternalEmploymentRequest request,
        CancellationToken ct)
    {
        var orgId=admin.OrganizationId
            ??throw new InvalidOperationException("目前管理者缺少 OrganizationId。");

        var strategy=db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async()=>{
            db.ChangeTracker.Clear();
            await using var tx=await db.Database.BeginTransactionAsync(ct);

            var user=await db.Users.FirstOrDefaultAsync(
                x=>x.UserId==userId&&x.OrganizationId==orgId,ct)
                ??throw new KeyNotFoundException("找不到 Internal User。");

            var identity=await db.UserIdentityProfiles.FirstOrDefaultAsync(
                x=>x.UserId==userId,ct)
                ??throw new InvalidOperationException("此帳號缺少 Identity Profile。");

            if(!identity.UserType.Equals(UserTypes.Internal,StringComparison.OrdinalIgnoreCase)
                ||!identity.EmploymentId.HasValue)
                throw new InvalidOperationException("此帳號沒有可維護的 v1.8 Employment。");

            var employment=await db.Employments.FirstOrDefaultAsync(
                x=>x.EmploymentId==identity.EmploymentId.Value&&x.OrganizationId==orgId,ct)
                ??throw new InvalidOperationException("找不到對應 Employment。");

            byte[] expected;
            try{expected=Convert.FromBase64String(request.EmploymentRowVersion);}
            catch{throw new InvalidOperationException("Employment RowVersion 格式不正確，請重新載入。");}
            if(!employment.RowVersion.SequenceEqual(expected))
                throw new InvalidOperationException("人事資料已被其他使用者更新，請重新載入後再操作。");

            var employeeNo=request.EmployeeNo.Trim();
            var email=string.IsNullOrWhiteSpace(request.Email)?null:request.Email.Trim();

            if(await db.Employments.AsNoTracking().AnyAsync(
                x=>x.OrganizationId==orgId
                    &&x.EmploymentId!=employment.EmploymentId
                    &&x.EmployeeNo==employeeNo,ct))
                throw new InvalidOperationException("此工號已被其他 Employment 使用。");

            if(email is not null&&await db.Users.AsNoTracking().AnyAsync(
                x=>x.OrganizationId==orgId&&x.UserId!=userId&&x.Email!=null&&x.Email==email,ct))
                throw new InvalidOperationException("此 Email 已被其他使用者使用。");

            var oldValues=new
            {
                user.EmployeeNo,user.DisplayName,user.Email,
                employment.HireDate,employment.TerminationDate,
                EmploymentRowVersion=Convert.ToBase64String(employment.RowVersion)
            };

            user.EmployeeNo=employeeNo;
            user.DisplayName=request.DisplayName.Trim();
            user.Email=email;
            user.UpdatedAt=DateTime.UtcNow;

            employment.EmployeeNo=employeeNo;
            employment.Email=email;
            employment.HireDate=request.HireDate;
            employment.TerminationDate=request.TerminationDate;

            db.AuditLogs.Add(new AuditLog
            {
                UserId=admin.UserId,
                EntityType="Employment",
                EntityId=employment.EmploymentId.ToString(),
                Action="EmploymentMasterUpdate",
                OldValues=JsonSerializer.Serialize(oldValues),
                NewValues=JsonSerializer.Serialize(new
                {
                    user.EmployeeNo,user.DisplayName,user.Email,
                    employment.HireDate,employment.TerminationDate
                }),
                CreatedAt=DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    private async Task EnsureIdentityBindingAvailableAsync(
        string? identityProvider,
        Guid? entraTenantId,
        Guid? entraObjectId,
        int targetUserId,
        CancellationToken ct)
    {
        if (!string.Equals(
                identityProvider,
                "EntraId",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!entraTenantId.HasValue
            || !entraObjectId.HasValue)
        {
            throw new InvalidOperationException(
                "Entra ID 綁定資料不完整。");
        }

        var duplicate =
            await db.UserIdentityProfiles
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.UserId != targetUserId
                        && x.EntraTenantId
                           == entraTenantId
                        && x.EntraObjectId
                           == entraObjectId,
                    ct);

        if (duplicate)
        {
            throw new InvalidOperationException(
                "此 EntraTenantId + EntraObjectId 已綁定其他系統使用者。");
        }
    }

    private static void ApplyIdentityBinding(
        UserIdentityProfile identity,
        string? identityProvider,
        Guid? entraTenantId,
        Guid? entraObjectId,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(
                identityProvider))
        {
            return;
        }

        identity.IdentityProvider =
            identityProvider;

        identity.EntraTenantId =
            entraTenantId;

        identity.EntraObjectId =
            entraObjectId;

        identity.UpdatedAt =
            now;
    }

    private static string NormalizeRoleCode(
        string role)
        => (role ?? "")
            .Trim()
            .ToLowerInvariant() switch
        {
            "visitor" => "visitor",
            "leader" => "leader",
            "admin" => "admin",
            "supervisor" => "supervisor",
            "government" => "supervisor",
            var value => value
        };

    private async Task PrepareScopeVersionAsync(
        int userId,
        DateOnly effectiveFrom,
        CancellationToken ct)
    {
        var rows =
            await db.UserDataScopes
                .Where(x => x.UserId == userId)
                .ToListAsync(ct);

        if (rows.Any(
                x => x.EffectiveFrom > effectiveFrom))
        {
            throw new InvalidOperationException(
                "此督導已有較晚生效的 Data Scope 排程，請先處理該排程後再異動。");
        }

        var previousDay =
            PreviousDay(effectiveFrom);

        foreach (var row in rows.Where(
                     x =>
                         x.EffectiveFrom < effectiveFrom
                         && (!x.EffectiveTo.HasValue
                             || x.EffectiveTo
                                >= effectiveFrom)))
        {
            row.EffectiveTo =
                previousDay;
        }

        var sameStart =
            rows.Where(
                    x =>
                        x.EffectiveFrom
                        == effectiveFrom)
                .ToList();

        if (sameStart.Count > 0)
        {
            db.UserDataScopes.RemoveRange(
                sameStart);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task PrepareCapabilityVersionAsync(
        int userId,
        string capabilityCode,
        DateOnly effectiveFrom,
        CancellationToken ct)
    {
        var rows =
            await db.UserCapabilities
                .Where(
                    x =>
                        x.UserId == userId
                        && x.CapabilityCode
                           == capabilityCode)
                .ToListAsync(ct);

        if (rows.Any(
                x => x.EffectiveFrom > effectiveFrom))
        {
            throw new InvalidOperationException(
                $"此督導已有較晚生效的 {capabilityCode} 排程，請先處理該排程後再異動。");
        }

        var previousDay =
            PreviousDay(effectiveFrom);

        foreach (var row in rows.Where(
                     x =>
                         x.EffectiveFrom < effectiveFrom
                         && (!x.EffectiveTo.HasValue
                             || x.EffectiveTo
                                >= effectiveFrom)))
        {
            row.EffectiveTo =
                previousDay;
        }

        var sameStart =
            rows.Where(
                    x =>
                        x.EffectiveFrom
                        == effectiveFrom)
                .ToList();

        if (sameStart.Count > 0)
        {
            db.UserCapabilities.RemoveRange(
                sameStart);
        }

        await db.SaveChangesAsync(ct);
    }

    private static DateOnly PreviousDay(
        DateOnly value)
    {
        if (value == DateOnly.MinValue)
        {
            throw new InvalidOperationException(
                "異動生效日不正確。");
        }

        return value.AddDays(-1);
    }

    private async Task<string> NewExternalUserCodeAsync(
        CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
        {
            var code =
                $"EXT-{Guid.NewGuid():N}"
                .ToUpperInvariant();

            if (!await db.UserIdentityProfiles
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.UserCode == code,
                        ct))
            {
                return code;
            }
        }

        throw new InvalidOperationException(
            "無法產生唯一的 External UserCode。");
    }

    private async Task AddCapabilityAsync(
        int userId,
        string capabilityCode,
        bool isAllowed,
        DateOnly effectiveFrom,
        DateOnly effectiveTo,
        int grantedByUserId,
        DateTime now,
        CancellationToken ct)
    {
        await db.UserCapabilities.AddAsync(
            new UserCapability
            {
                UserId =
                    userId,

                CapabilityCode =
                    capabilityCode,

                IsAllowed =
                    isAllowed,

                EffectiveFrom =
                    effectiveFrom,

                EffectiveTo =
                    effectiveTo,

                GrantedByUserId =
                    grantedByUserId,

                CreatedAt =
                    now
            },
            ct);
    }
}
