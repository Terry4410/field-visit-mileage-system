using System.Data;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

internal static class V180OptionCMergePublicationWriter
{
    internal static async Task ExecuteAsync(
        SqlConnection db, SqlTransaction tx, Guid publicId, int organizationId,
        int reviewer, Guid decisionKey, string reason,
        V180OptionCLiveApprovalEvidence auth, CancellationToken ct)
    {
        if (tx.IsolationLevel != IsolationLevel.Serializable
            || !auth.RequesterEligible || !auth.IndependentAdminEligible
            || !auth.OrganizationMatches || !auth.TeamMatches
            || reviewer <= 0 || decisionKey == Guid.Empty
            || string.IsNullOrWhiteSpace(reason) || reason != reason.Trim())
            throw new InvalidOperationException("OPTIONC_MERGE_AUTHORIZATION_REQUIRED");
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(db);
        const string sql = """
        SET XACT_ABORT ON;
        DECLARE @request bigint,@source int,@survivor int,@org int,@team int,@requester int,
            @sourceVersion binary(8),@survivorVersion binary(8),@requestVersion binary(8),
            @proposed nvarchar(max),@detailEvidence nvarchar(max),@stageVersion binary(8),
            @currentVersion int,@now datetime2(3)=SYSUTCDATETIME(),
            @correlation uniqueidentifier=NEWID();
        SELECT @request=r.ChangeRequestId,@proposed=r.ProposedJson,@requestVersion=r.RowVersion,
            @org=r.OrganizationId,@team=r.TeamId,@requester=r.RequestedByUserId
        FROM dbo.ChangeRequests r WITH(UPDLOCK,HOLDLOCK)
        JOIN dbo.LocationMergeDetails d ON d.ChangeRequestId=r.ChangeRequestId
        WHERE r.RequestPublicId=@publicId AND r.OrganizationId=@requestedOrg
          AND r.Status=N'Pending' AND r.RequestedByUserId<>@reviewer;
        IF @request IS NULL THROW 51470,N'OPTIONC_MERGE_REQUEST_NOT_PENDING',1;
        SELECT @source=SourceLocationId,@survivor=SurvivorLocationId,
            @sourceVersion=SourceLocationRowVersion,@survivorVersion=SurvivorLocationRowVersion,
            @detailEvidence=EvidenceJson
        FROM dbo.LocationMergeDetails WITH(UPDLOCK,HOLDLOCK)
        WHERE ChangeRequestId=@request;
        IF @source IS NULL OR @survivor IS NULL OR @source=@survivor
            THROW 51471,N'OPTIONC_MERGE_DETAIL_INVALID',1;
        IF @source < @survivor
        BEGIN
            SELECT RowVersion FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK) WHERE LocationId=@source;
            SELECT RowVersion FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK) WHERE LocationId=@survivor;
        END
        ELSE
        BEGIN
            SELECT RowVersion FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK) WHERE LocationId=@survivor;
            SELECT RowVersion FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK) WHERE LocationId=@source;
        END;
        IF EXISTS(SELECT 1 FROM dbo.ChangeRequestEvents WITH(UPDLOCK,HOLDLOCK)
                  WHERE DecisionKey=@decision)
            THROW 51472,N'OPTIONC_DECISION_REPLAY',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.Locations WHERE LocationId=@source
              AND OrganizationId=@org AND TeamId=@team AND RowVersion=@sourceVersion
              AND IsActive=1 AND ApprovalStatus=N'Approved' AND LocationType=N'Customer'
              AND CreatedByUserId=@requester)
            THROW 51473,N'OPTIONC_MERGE_SOURCE_CONFLICT',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.Locations WHERE LocationId=@survivor
              AND OrganizationId=@org AND TeamId=@team AND RowVersion=@survivorVersion
              AND IsActive=1 AND ApprovalStatus=N'Approved' AND LocationType=N'Customer')
            THROW 51474,N'OPTIONC_MERGE_SURVIVOR_CONFLICT',1;
        SELECT @stageVersion=RowVersion FROM dbo.LocationPublicationStages WITH(UPDLOCK,HOLDLOCK)
         WHERE ChangeRequestId=@request AND LocationId=@survivor AND ExpectedLocationRowVersion=@survivorVersion
           AND StageStatus=N'ReadyForPublish' AND DuplicateReviewStatus=N'Cleared'
           AND DuplicateEvidenceSha256=ProposedSha256
           AND ProposedSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),@proposed))
           AND (GeocodeStatus=N'NotRequired' OR
                (GeocodeStatus=N'Succeeded' AND GeocodeAddressSha256=
                  HASHBYTES('SHA2_256',CONVERT(varbinary(max),JSON_VALUE(ProposedJson,N'$.Address')))));
        IF @stageVersion IS NULL THROW 51475,N'OPTIONC_MERGE_STAGE_NOT_READY',1;
        IF (SELECT COUNT(*) FROM dbo.ProjectLocations WHERE LocationId=@source AND IsActive=1
            AND EXISTS(SELECT 1 FROM dbo.ProjectLocations p2 WHERE p2.ProjectId=ProjectLocations.ProjectId AND p2.LocationId=@survivor AND p2.IsActive=1))>0
            THROW 51476,N'OPTIONC_MERGE_REFERENCE_CONFLICT',1;
        IF EXISTS(SELECT 1 FROM dbo.UserFavoriteLocations f WHERE f.LocationId=@source
            AND EXISTS(SELECT 1 FROM dbo.UserFavoriteLocations f2 WHERE f2.UserId=f.UserId AND f2.LocationId=@survivor))
            THROW 51477,N'OPTIONC_MERGE_FAVORITE_CONFLICT',1;
        SELECT @currentVersion=VersionNumber FROM dbo.LocationPublishedVersions WITH(UPDLOCK,HOLDLOCK)
            WHERE LocationId=@survivor AND IsCurrent=1;
        IF @currentVersion IS NULL THROW 51478,N'OPTIONC_MERGE_VERSION_MISSING',1;
        UPDATE dbo.Locations SET LocationName=JSON_VALUE(@proposed,N'$.LocationName'),
            City=JSON_VALUE(@proposed,N'$.City'),District=JSON_VALUE(@proposed,N'$.District'),
            Address=JSON_VALUE(@proposed,N'$.Address'),PlusCode=JSON_VALUE(@proposed,N'$.PlusCode'),
            TaxId=JSON_VALUE(@proposed,N'$.TaxId'),MasterNote=JSON_VALUE(@proposed,N'$.MasterNote')
         WHERE LocationId=@survivor AND RowVersion=@survivorVersion AND IsActive=1;
        IF @@ROWCOUNT<>1 THROW 51479,N'OPTIONC_MERGE_SURVIVOR_UPDATE_CONFLICT',1;
        UPDATE dbo.ProjectLocations SET LocationId=@survivor,UpdatedAt=@now WHERE LocationId=@source AND IsActive=1;
        UPDATE dbo.UserFavoriteLocations SET LocationId=@survivor WHERE LocationId=@source;
        UPDATE dbo.GovernmentLocationMasters SET MatchedLocationId=@survivor,UpdatedAt=@now WHERE MatchedLocationId=@source;
        UPDATE dbo.Locations SET IsActive=0,InactivatedAt=@now,InactivatedByUserId=@reviewer,
            DuplicateOfLocationId=@survivor,DuplicateReason=N'Approved Merge',UpdatedAt=@now
         WHERE LocationId=@source AND RowVersion=@sourceVersion AND IsActive=1;
        IF @@ROWCOUNT<>1 THROW 51480,N'OPTIONC_MERGE_SOURCE_UPDATE_CONFLICT',1;
        UPDATE dbo.LocationPublishedVersions SET IsCurrent=0 WHERE LocationId=@survivor AND IsCurrent=1;
        INSERT dbo.LocationPublishedVersions(LocationId,VersionNumber,ChangeRequestId,IsCurrent,
            PublishedJson,PublishedSha256,PublishedAt,PublishedByUserId,SourceLocationRowVersion)
        SELECT @survivor,@currentVersion+1,@request,1,@proposed,
            HASHBYTES('SHA2_256',CONVERT(varbinary(max),@proposed)),@now,@reviewer,@survivorVersion;
        UPDATE dbo.LocationPublicationStages SET StageStatus=N'Published',UpdatedAt=@now WHERE ChangeRequestId=@request AND RowVersion=@stageVersion AND StageStatus=N'ReadyForPublish';
        IF @@ROWCOUNT<>1 THROW 51481,N'OPTIONC_MERGE_STAGE_CONFLICT',1;
        UPDATE dbo.ChangeRequests SET Status=N'Applied',ReviewedByUserId=@reviewer,ReviewedAt=@now,AppliedAt=@now,ReviewReason=@reason
         WHERE ChangeRequestId=@request AND RowVersion=@requestVersion AND Status=N'Pending';
        IF @@ROWCOUNT<>1 THROW 51482,N'OPTIONC_MERGE_REQUEST_CONFLICT',1;
        INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
        VALUES(@request,N'Approved',@reviewer,@now,@correlation,@decision,JSON_QUERY(@detailEvidence));
        INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
        VALUES(@request,N'Applied',@reviewer,@now,@correlation,NULL,JSON_QUERY(@detailEvidence));
        """;
        await using var command = new SqlCommand(sql, db, tx);
        command.Parameters.AddWithValue("@publicId", publicId);
        command.Parameters.AddWithValue("@requestedOrg", organizationId);
        command.Parameters.AddWithValue("@reviewer", reviewer);
        command.Parameters.AddWithValue("@decision", decisionKey);
        command.Parameters.AddWithValue("@reason", reason);
        await command.ExecuteNonQueryAsync(ct);
    }
}
