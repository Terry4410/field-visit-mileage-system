using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

/// <summary>
/// Mileage-rate version rules for Owner Pre-UAT.
/// Effective dates are administrator-owned. Validation never derives or rewrites
/// the neighboring version's dates, which keeps maintenance predictable for IT.
/// </summary>
public static class V180MileageRateVersionRules
{
    public static string NormalizeVehicleType(string? vehicleType)
    {
        var raw=string.IsNullOrWhiteSpace(vehicleType)?"Motorcycle":vehicleType;
        return V180MileageCanonicalization.ToDbRequestedVehicleType(
            V180MileageCanonicalization.CanonicalVehicleType(raw));
    }

    public static void Validate(string? ruleName,decimal ratePerKm,DateOnly effectiveFrom,DateOnly? effectiveTo)
    {
        if(string.IsNullOrWhiteSpace(ruleName))throw new InvalidOperationException("規則名稱為必填。");
        if(ratePerKm<0)throw new InvalidOperationException("每公里補助不可小於 0。");
        if(effectiveTo.HasValue&&effectiveTo.Value<effectiveFrom)
            throw new InvalidOperationException("失效日不可早於生效日。");
    }

    public static void EnsureNoOverlap(
        IEnumerable<MileageRateRule> series,
        int? excludedMileageRateRuleId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        var requestedTo=effectiveTo??DateOnly.MaxValue;
        var conflict=series
            .Where(x=>x.IsActive)
            .Where(x=>!excludedMileageRateRuleId.HasValue||x.MileageRateRuleId!=excludedMileageRateRuleId.Value)
            .FirstOrDefault(x=>x.EffectiveFrom<=requestedTo&&effectiveFrom<=(x.EffectiveTo??DateOnly.MaxValue));
        if(conflict is not null)
            throw new InvalidOperationException($"同一交通工具的補助費率有效期間不可重疊；與「{conflict.RuleName}」（{conflict.EffectiveFrom:yyyy/MM/dd}～{(conflict.EffectiveTo.HasValue ? conflict.EffectiveTo.Value.ToString("yyyy/MM/dd") : "無期限")}）期間重疊。請先調整舊費率失效日或本次生效日，已核准行程不會自動重算。");
    }
}
