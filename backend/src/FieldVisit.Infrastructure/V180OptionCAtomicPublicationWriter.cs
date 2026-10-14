using System.Data;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Nondeploy-only, UNREGISTERED publication writer. Only an orchestrator
/// that has freshly validated live requester and independent admin privileges
/// may invoke this with its OWN serializable transaction. No endpoint, DI,
/// schema migration or feature activation is provided.
/// </summary>
internal static class V180OptionCAtomicPublicationWriter
{
    internal static async Task ExecuteAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid requestPublicId, int organizationId, int liveReviewerUserId,
        Guid decisionKey, string reason,
        V180OptionCLiveApprovalEvidence authorization, CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open
            || transaction.Connection != connection
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || requestPublicId == Guid.Empty || decisionKey == Guid.Empty
            || organizationId <= 0 || liveReviewerUserId <= 0
            || authorization is null
            || !authorization.RequesterEligible
            || !authorization.IndependentAdminEligible
            || !authorization.OrganizationMatches
            || !authorization.TeamMatches
            || string.IsNullOrWhiteSpace(reason) || reason != reason.Trim()
            || reason.Length > 1000)
            throw new InvalidOperationException("OPTIONC_ATOMIC_PUBLICATION_DENIED");

        // Deliberately limited to isolated disposable SQL Server. It is NOT a
        // production query and cannot be invoked against a UAT database.
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection);

        // MergeFields uses the additive detail row for the second Location identity.
        // It remains on the same canonical request, Stage and Serializable transaction.
        await using (var mergeProbe = new SqlCommand(
            "SELECT COUNT_BIG(1) FROM dbo.LocationMergeDetails d JOIN dbo.ChangeRequests r ON r.ChangeRequestId=d.ChangeRequestId WHERE r.RequestPublicId=@publicId AND r.OrganizationId=@org AND r.Status=N'Pending';",
            connection, transaction))
        {
            mergeProbe.Parameters.AddWithValue("@publicId", requestPublicId);
            mergeProbe.Parameters.AddWithValue("@org", organizationId);
            if (Convert.ToInt64(await mergeProbe.ExecuteScalarAsync(ct)) == 1)
            {
                await V180OptionCMergePublicationWriter.ExecuteAsync(
                    connection, transaction, requestPublicId, organizationId,
                    liveReviewerUserId, decisionKey, reason, authorization, ct);
                return;
            }
        }

        const string sql = """
            SET XACT_ABORT ON;
            DECLARE @requestId bigint, @locationId int, @team int,
                @requester int, @expected binary(8), @requestVersion binary(8),
                @locationVersion binary(8), @proposed nvarchar(max),
                @currentVersion int, @stageHash binary(32), @stageVersion binary(8),
                @now datetime2(3)=SYSUTCDATETIME(), @correlation uniqueidentifier=NEWID();

            -- Lock order is fixed: canonical request -> source -> stage -> ledger.
            SELECT @requestId=ChangeRequestId,
                   @locationId=TRY_CONVERT(int,EntityId), @team=TeamId,
                   @requester=RequestedByUserId,
                   @expected=ExpectedEntityRowVersion,
                   @requestVersion=RowVersion, @proposed=ProposedJson
            FROM dbo.ChangeRequests WITH(UPDLOCK,HOLDLOCK)
            WHERE RequestPublicId=@publicId AND OrganizationId=@org
              AND EntityKind=N'Location'
              AND OperationCode=N'UpdatePublishedLocation'
              AND RiskCode=N'High' AND Status=N'Pending'
              AND ReviewedByUserId IS NULL AND AppliedAt IS NULL;

            IF @requestId IS NULL OR @locationId IS NULL OR @team IS NULL
                OR @team<=0 OR @requester IS NULL OR @requester=@reviewer
                THROW 51350,N'OPTIONC_REQUEST_OR_INDEPENDENCE_DENIED',1;

            SELECT @locationVersion=RowVersion
            FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK)
            WHERE LocationId=@locationId AND OrganizationId=@org
              AND TeamId=@team AND CreatedByUserId=@requester
              AND LocationType=N'Customer'
              AND ApprovalStatus=N'Approved' AND IsActive=1;
            IF @locationVersion IS NULL OR @locationVersion<>@expected
                THROW 51351,N'OPTIONC_SOURCE_CHANGED',1;

            SELECT @stageHash=ProposedSha256,@stageVersion=RowVersion
            FROM dbo.LocationPublicationStages WITH(UPDLOCK,HOLDLOCK)
            WHERE ChangeRequestId=@requestId AND LocationId=@locationId
              AND OrganizationId=@org AND TeamId=@team
              AND ExpectedLocationRowVersion=@locationVersion
              AND ProposedJson=@proposed AND StageStatus=N'ReadyForPublish'
              AND DuplicateReviewStatus=N'Cleared'
              AND DuplicateEvidenceSha256=ProposedSha256
              AND GeocodeStatus=N'Succeeded'
              AND GeocodeAddressSha256=HASHBYTES('SHA2_256',
                  CONVERT(varbinary(max),JSON_VALUE(ProposedJson,N'$.Address')))
              AND Latitude IS NOT NULL AND Longitude IS NOT NULL;
            IF @stageVersion IS NULL
                THROW 51352,N'OPTIONC_STAGE_NOT_READY',1;
            -- A staged SHA must be the exact request payload. The canonical
            -- request and staged version cannot be independently repointed.
            IF @stageHash IS NULL OR @stageHash <>
                HASHBYTES('SHA2_256',CONVERT(varbinary(max),@proposed))
                THROW 51359,N'OPTIONC_PROPOSAL_HASH_DRIFT',1;
            IF ISJSON(@proposed)<>1
                OR NULLIF(LTRIM(RTRIM(JSON_VALUE(@proposed,N'$.LocationName'))),N'') IS NULL
                OR NULLIF(LTRIM(RTRIM(JSON_VALUE(@proposed,N'$.Address'))),N'') IS NULL
                THROW 51360,N'OPTIONC_PUBLISHED_IDENTITY_INVALID',1;

            IF EXISTS(SELECT 1 FROM dbo.ChangeRequestEvents WITH(UPDLOCK,HOLDLOCK)
                WHERE DecisionKey=@decision)
                THROW 51353,N'OPTIONC_DECISION_REPLAY',1;

            SELECT @currentVersion=VersionNumber
            FROM dbo.LocationPublishedVersions WITH(UPDLOCK,HOLDLOCK)
            WHERE LocationId=@locationId AND IsCurrent=1;
            IF @currentVersion IS NULL
                THROW 51354,N'OPTIONC_MISSING_BASELINE_VERSION',1;

            -- No new LocationId: trip and snapshot foreign keys remain stable.
            UPDATE dbo.LocationPublishedVersions SET IsCurrent=0
            WHERE LocationId=@locationId AND VersionNumber=@currentVersion
                AND IsCurrent=1;
            IF @@ROWCOUNT<>1 THROW 51355,N'OPTIONC_LEDGER_DRIFT',1;

            UPDATE dbo.Locations SET
                LocationName=JSON_VALUE(@proposed,N'$.LocationName'),
                City=JSON_VALUE(@proposed,N'$.City'),
                District=JSON_VALUE(@proposed,N'$.District'),
                Address=JSON_VALUE(@proposed,N'$.Address'),
                PlusCode=JSON_VALUE(@proposed,N'$.PlusCode'),
                TaxId=JSON_VALUE(@proposed,N'$.TaxId'),
                MasterNote=JSON_VALUE(@proposed,N'$.MasterNote')
            WHERE LocationId=@locationId AND OrganizationId=@org
              AND TeamId=@team AND CreatedByUserId=@requester
              AND ApprovalStatus=N'Approved' AND IsActive=1
              AND RowVersion=@locationVersion;
            IF @@ROWCOUNT<>1 THROW 51356,N'OPTIONC_LOCATION_UPDATE_CONFLICT',1;

            INSERT dbo.LocationPublishedVersions(
                LocationId,VersionNumber,ChangeRequestId,IsCurrent,
                PublishedJson,PublishedSha256,PublishedAt,
                PublishedByUserId,SourceLocationRowVersion)
            VALUES(@locationId,@currentVersion+1,@requestId,1,
                @proposed,@stageHash,@now,@reviewer,@locationVersion);

            UPDATE dbo.LocationPublicationStages SET
                StageStatus=N'Published',UpdatedAt=@now
            WHERE ChangeRequestId=@requestId AND RowVersion=@stageVersion
                AND StageStatus=N'ReadyForPublish';
            IF @@ROWCOUNT<>1 THROW 51357,N'OPTIONC_STAGE_CHANGED',1;

            UPDATE dbo.ChangeRequests SET Status=N'Applied',
                ReviewedByUserId=@reviewer,ReviewedAt=@now,
                AppliedAt=@now,ReviewReason=@reason
            WHERE ChangeRequestId=@requestId AND RowVersion=@requestVersion
                AND Status=N'Pending' AND RequestedByUserId<>@reviewer;
            IF @@ROWCOUNT<>1 THROW 51358,N'OPTIONC_REQUEST_CHANGED',1;

            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES(@requestId,N'Approved',@reviewer,@now,@correlation,@decision);
            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES(@requestId,N'Applied',@reviewer,@now,@correlation,NULL);
            """;
        await using var cmd = new SqlCommand(sql, connection, transaction)
        { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@publicId", requestPublicId);
        cmd.Parameters.AddWithValue("@org", organizationId);
        cmd.Parameters.AddWithValue("@reviewer", liveReviewerUserId);
        cmd.Parameters.AddWithValue("@decision", decisionKey);
        cmd.Parameters.AddWithValue("@reason", reason);
        // Transaction ownership, COMMIT and unknown-COMMIT reconciliation are
        // deliberately outside this writer; no automatic retries.
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
