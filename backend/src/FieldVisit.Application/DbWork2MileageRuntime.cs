using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public sealed record MileageRateRuntimeDto(
    int MileageRateRuleId,
    int? OrganizationId,
    string RuleName,
    string VehicleType,
    decimal RatePerKm,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    string RowVersion);

public sealed record UpdateMileageRateRuntimeRequest(
    string RuleName,
    string VehicleType,
    decimal RatePerKm,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    bool AcknowledgeHistoricalImpact,
    string RowVersion);

public interface IFinalizedMileageRateImpactReader
{
    Task<(int Count, DateOnly? FirstVisitDate, DateOnly? LastVisitDate)> GetImpactAsync(
        int? organizationId,
        string vehicleType,
        DateOnly effectiveFrom,
        CancellationToken ct);
}

public interface ITransactionBoundary
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct);
}

public static class DbWork2MileageRateRules
{
    public static string NormalizeVehicleType(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "" or null => "MOTORCYCLE",
            "MOTORCYCLE" => "MOTORCYCLE",
            "CAR" => "CAR",
            _ => throw new InvalidOperationException("VehicleType 只允許 MOTORCYCLE 或 CAR。")
        };

    public static byte[] RequireRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("RowVersion 為必填。");

        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch
        {
            throw new InvalidOperationException("RowVersion 格式不正確。");
        }
    }

    public static void EnsureRowVersion(byte[] current, string? expectedBase64)
    {
        var expected = RequireRowVersion(expectedBase64);
        if (!current.SequenceEqual(expected))
            throw new InvalidOperationException("ROWVERSION_CONFLICT：資料已被其他使用者修改，請重新整理。");
    }

    public static void RejectCallerEffectiveTo(DateOnly? effectiveTo)
    {
        if (effectiveTo.HasValue)
            throw new InvalidOperationException("MILEAGE_RATE_EFFECTIVE_TO_DB_AUTHORITY：EffectiveTo 由資料庫衍生；呼叫端不得提供非 null 值。");
    }

    public static void Validate(string ruleName, decimal ratePerKm)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
            throw new InvalidOperationException("規則名稱為必填。");
        if (ratePerKm < 0)
            throw new InvalidOperationException("每公里補助不可小於 0。");
    }
}

public sealed class MileageRateRuntimeService(
    ICurrentUserService current,
    IMileageRepository mileage,
    IFinalizedMileageRateImpactReader finalizedImpact,
    IWorkflowRepository workflow,
    IUnitOfWork uow,
    ITransactionBoundary transactions)
{
    public async Task<List<MileageRateRuntimeDto>> RatesAsync(CancellationToken ct)
    {
        var user = current.GetRequired();
        return (await mileage.GetRatesAsync(user, ct)).Select(Map).ToList();
    }

    public async Task<MileageRateImpactDto> RateImpactAsync(
        DateOnly effectiveFrom,
        string? vehicleType,
        CancellationToken ct)
    {
        var user = RequireAdmin();
        var vehicle = DbWork2MileageRateRules.NormalizeVehicleType(vehicleType);
        var impact = await finalizedImpact.GetImpactAsync(user.OrganizationId, vehicle, effectiveFrom, ct);
        return new MileageRateImpactDto(
            effectiveFrom,
            vehicle,
            impact.Count,
            impact.FirstVisitDate,
            impact.LastVisitDate,
            impact.Count > 0);
    }

    public Task<MileageRateRuntimeDto> CreateRateAsync(
        CreateMileageRateRequest request,
        CancellationToken ct)
    {
        return transactions.ExecuteAsync(async innerCt =>
        {
            var user = RequireAdmin();
            DbWork2MileageRateRules.RejectCallerEffectiveTo(request.EffectiveTo);
            DbWork2MileageRateRules.Validate(request.RuleName, request.RatePerKm);
            var vehicle = DbWork2MileageRateRules.NormalizeVehicleType(request.VehicleType);

            var series = await mileage.GetRateSeriesAsync(user.OrganizationId, vehicle, false, innerCt);
            if (series.Any(x => x.IsActive && x.EffectiveFrom == request.EffectiveFrom))
                throw new InvalidOperationException("同一車種不可有兩個同日生效的費率版本。");

            await EnsureHistoricalImpactAcknowledgedAsync(
                user.OrganizationId,
                vehicle,
                request.EffectiveFrom,
                request.AcknowledgeHistoricalImpact,
                innerCt);

            var row = new MileageRateRule
            {
                OrganizationId = user.OrganizationId,
                RuleName = request.RuleName.Trim(),
                VehicleType = vehicle,
                RatePerKm = request.RatePerKm,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = null,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = user.UserId
            };

            await mileage.AddRateAsync(row, innerCt);
            await uow.SaveChangesAsync(innerCt);
            await workflow.AddAuditAsync(Audit(
                user.UserId,
                row.MileageRateRuleId.ToString(),
                "MileageRateCreate",
                new
                {
                    request.RuleName,
                    request.RatePerKm,
                    request.EffectiveFrom,
                    request.AcknowledgeHistoricalImpact
                }), innerCt);
            await uow.SaveChangesAsync(innerCt);

            var persisted = await mileage.GetRateAsync(row.MileageRateRuleId, false, innerCt)
                ?? throw new InvalidOperationException("MileageRate insert succeeded but DB-derived row could not be reloaded.");
            return Map(persisted);
        }, ct);
    }

    public async Task<MileageRateRuntimeDto> UpdateRateAsync(
        int mileageRateRuleId,
        UpdateMileageRateRuntimeRequest request,
        CancellationToken ct)
    {
        var user = RequireAdmin();
        DbWork2MileageRateRules.RejectCallerEffectiveTo(request.EffectiveTo);
        DbWork2MileageRateRules.Validate(request.RuleName, request.RatePerKm);
        DbWork2MileageRateRules.RequireRowVersion(request.RowVersion);

        var row = await mileage.GetRateAsync(mileageRateRuleId, true, ct)
            ?? throw new KeyNotFoundException("找不到補助費率。");
        EnsureAuthority(user, row);
        DbWork2MileageRateRules.EnsureRowVersion(row.RowVersion, request.RowVersion);

        var oldEffectiveFrom = row.EffectiveFrom;
        var vehicle = DbWork2MileageRateRules.NormalizeVehicleType(request.VehicleType);
        var series = await mileage.GetRateSeriesAsync(user.OrganizationId, vehicle, false, ct);
        if (request.IsActive && series.Any(x =>
                x.IsActive
                && x.MileageRateRuleId != mileageRateRuleId
                && x.EffectiveFrom == request.EffectiveFrom))
        {
            throw new InvalidOperationException("同一車種不可有兩個同日生效的費率版本。");
        }

        var financialScheduleChanged =
            row.RatePerKm != request.RatePerKm
            || row.EffectiveFrom != request.EffectiveFrom
            || row.IsActive != request.IsActive
            || !string.Equals(row.VehicleType, vehicle, StringComparison.OrdinalIgnoreCase);

        if (financialScheduleChanged)
        {
            var impactFrom = oldEffectiveFrom <= request.EffectiveFrom
                ? oldEffectiveFrom
                : request.EffectiveFrom;
            await EnsureHistoricalImpactAcknowledgedAsync(
                user.OrganizationId,
                vehicle,
                impactFrom,
                request.AcknowledgeHistoricalImpact,
                ct);
        }

        row.RuleName = request.RuleName.Trim();
        row.VehicleType = vehicle;
        row.RatePerKm = request.RatePerKm;
        row.EffectiveFrom = request.EffectiveFrom;
        row.IsActive = request.IsActive;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = user.UserId;

        await workflow.AddAuditAsync(Audit(
            user.UserId,
            mileageRateRuleId.ToString(),
            "MileageRateUpdate",
            new
            {
                request.RuleName,
                request.RatePerKm,
                request.EffectiveFrom,
                request.IsActive,
                request.AcknowledgeHistoricalImpact
            }), ct);
        await uow.SaveChangesAsync(ct);

        var persisted = await mileage.GetRateAsync(mileageRateRuleId, false, ct)
            ?? throw new InvalidOperationException("MileageRate update succeeded but DB-derived row could not be reloaded.");
        return Map(persisted);
    }

    public async Task DeleteRateAsync(
        int mileageRateRuleId,
        bool acknowledgeHistoricalImpact,
        string rowVersion,
        CancellationToken ct)
    {
        var user = RequireAdmin();
        DbWork2MileageRateRules.RequireRowVersion(rowVersion);
        var row = await mileage.GetRateAsync(mileageRateRuleId, true, ct)
            ?? throw new KeyNotFoundException("找不到補助費率。");
        EnsureAuthority(user, row);
        DbWork2MileageRateRules.EnsureRowVersion(row.RowVersion, rowVersion);
        if (!row.IsActive) return;

        await EnsureHistoricalImpactAcknowledgedAsync(
            user.OrganizationId,
            row.VehicleType,
            row.EffectiveFrom,
            acknowledgeHistoricalImpact,
            ct);

        row.IsActive = false;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = user.UserId;
        row.InactivatedAt = DateTime.UtcNow;
        row.InactivatedByUserId = user.UserId;

        await workflow.AddAuditAsync(Audit(
            user.UserId,
            mileageRateRuleId.ToString(),
            "MileageRateDeactivate",
            new { mileageRateRuleId, acknowledgeHistoricalImpact }), ct);
        await uow.SaveChangesAsync(ct);
    }

    private async Task EnsureHistoricalImpactAcknowledgedAsync(
        int? organizationId,
        string vehicleType,
        DateOnly effectiveFrom,
        bool acknowledged,
        CancellationToken ct)
    {
        var impact = await finalizedImpact.GetImpactAsync(
            organizationId,
            DbWork2MileageRateRules.NormalizeVehicleType(vehicleType),
            effectiveFrom,
            ct);
        if (impact.Count > 0 && !acknowledged)
            throw new InvalidOperationException($"此生效日期之後已有 {impact.Count} 筆已核准且具有費率快照的行程；費率主檔異動不會自動重算既有 Snapshot。請確認歷史影響後再執行。");
    }

    private CurrentUserDto RequireAdmin()
    {
        var user = current.GetRequired();
        if (!user.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("目前角色無權執行此操作。");
        return user;
    }

    private static void EnsureAuthority(CurrentUserDto user, MileageRateRule row)
    {
        if (row.OrganizationId != user.OrganizationId)
            throw new UnauthorizedAccessException("無權維護其他組織費率。");
    }

    private static MileageRateRuntimeDto Map(MileageRateRule x) => new(
        x.MileageRateRuleId,
        x.OrganizationId,
        x.RuleName,
        x.VehicleType,
        x.RatePerKm,
        x.EffectiveFrom,
        x.EffectiveTo,
        x.IsActive,
        Convert.ToBase64String(x.RowVersion ?? []));

    private static AuditLog Audit(
        int userId,
        string? id,
        string action,
        object value) => new()
        {
            UserId = userId,
            EntityType = "MileageRateRule",
            EntityId = id,
            Action = action,
            NewValues = System.Text.Json.JsonSerializer.Serialize(value),
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
}
