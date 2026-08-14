namespace FieldVisit.Domain.Entities;

public static class GovernmentLocationSourceTypes
{
    public const string OpenData = "OpenData";
    public const string FileImport = "FileImport";
}

public static class GovernmentLocationSyncStatuses
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Partial = "Partial";
}

public static class GovernmentLocationReviewStatuses
{
    public const string PendingReview = "PendingReview";
    public const string Matched = "Matched";
    public const string Ignored = "Ignored";
}

/// <summary>
/// Configuration of an external government/open-data location source.
///
/// Source data is a candidate/reference source only. It must never directly
/// overwrite Locations, VisitTripStops or approved Snapshots.
/// </summary>
public sealed class GovernmentLocationSource
{
    public int GovernmentLocationSourceId { get; set; }

    public string SourceCode { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceType { get; set; } = GovernmentLocationSourceTypes.OpenData;

    public string? SourceUrl { get; set; }
    public string? LicenseNote { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime? LastSyncStartedAt { get; set; }
    public DateTime? LastSyncCompletedAt { get; set; }
    public string? LastSyncStatus { get; set; }
    public string? LastSyncMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Explicit service-area configuration.
///
/// v1.7 does not assume that every government source covers all of Taiwan.
/// District may be null to represent a city-wide configured area.
/// </summary>
public sealed class GovernmentLocationSourceArea
{
    public int GovernmentLocationSourceAreaId { get; set; }
    public int GovernmentLocationSourceId { get; set; }

    public string City { get; set; } = "";
    public string? District { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Cached candidate record from a government/open-data source.
///
/// A source change moves the candidate back to PendingReview; later phases
/// may match it to an application Location only after explicit review.
/// </summary>
public sealed class GovernmentLocationMaster
{
    public long GovernmentLocationMasterId { get; set; }
    public int GovernmentLocationSourceId { get; set; }

    public string SourceRecordKey { get; set; } = "";
    public string? TaxId { get; set; }

    public string LocationName { get; set; } = "";
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? Address { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public string SourceHash { get; set; } = "";

    public string ReviewStatus { get; set; } =
        GovernmentLocationReviewStatuses.PendingReview;

    public int? MatchedLocationId { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? SourceUpdatedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Personal-only favorite application Location.
/// Favorites are never shared between users.
/// </summary>
public sealed class UserFavoriteLocation
{
    public long UserFavoriteLocationId { get; set; }

    public int UserId { get; set; }
    public int LocationId { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
}
