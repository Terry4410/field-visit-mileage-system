using System.Data;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Unregistered NONDEPLOY candidate. Invocable only by a future orchestrator
/// which obtains live HR/role/team/appointment evidence inside this SAME
/// Serializable transaction. Never grants its own approval or owns COMMIT.
/// Only CreateLocation/High and UpdatePublishedLocation/Low are supported.
/// </summary>
internal static class V180OptionCCreateLowRiskPublicationWriter
{
    internal static async Task ExecuteAsync(
        SqlConnection connection, SqlTransaction transaction, Guid requestPublicId,
        int organizationId, int finalReviewerUserId, Guid decisionKey,
        string reason, bool transactionBoundLiveAuthorization,
        bool independentAdminForCreate, CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open || transaction.Connection != connection
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || requestPublicId == Guid.Empty || decisionKey == Guid.Empty
            || organizationId <= 0 || finalReviewerUserId <= 0
            || !transactionBoundLiveAuthorization
            || string.IsNullOrWhiteSpace(reason) || reason != reason.Trim()
            || reason.Length > 1000)
            throw new InvalidOperationException("OPTIONC_CREATE_LOW_ATOMIC_DENIED");
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection);
        await V180OptionCPublicationCatalogRules.RequireAsync(connection, ct);

        const string sql = """
            SET XACT_ABORT ON;
            DECLARE @requestId bigint, @locationId int, @team int,
                @requester int, @expected binary(8), @requestVersion binary(8),
                @locationVersion binary(8), @proposed nvarchar(max),
                @operation nvarchar(80), @risk nvarchar(20),
                @status nvarchar(20), @active bit, @sourceOwner int,
                @stageHash binary(32), @stageVersion binary(8),
                @geocode nvarchar(20), @duplicate nvarchar(20),
                @latitude decimal(10,7), @longitude decimal(10,7),
                @leader int, @leaderKey uniqueidentifier,
                @currentVersion int, @reservationVersion binary(8),
                @now datetime2(3)=SYSUTCDATETIME(), @correlation uniqueidentifier=NEWID();

            IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions
                WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')
                THROW 51600,N'OPTIONC_CREATE_LOW_SCHEMA_NOT_READY',1;

            SELECT @requestId=ChangeRequestId,@locationId=TRY_CONVERT(int,EntityId),
                @team=TeamId,@requester=RequestedByUserId,
                @expected=ExpectedEntityRowVersion,@requestVersion=RowVersion,
                @proposed=ProposedJson,@operation=OperationCode,@risk=RiskCode
            FROM dbo.ChangeRequests WITH(UPDLOCK,HOLDLOCK)
            WHERE RequestPublicId=@publicId AND OrganizationId=@org
              AND EntityKind=N'Location' AND Status=N'Pending'
              AND ReviewedByUserId IS NULL AND AppliedAt IS NULL
              AND ((OperationCode=N'CreateLocation' AND RiskCode=N'High'
                    AND BeforeJson IS NULL)
                OR (OperationCode=N'UpdatePublishedLocation' AND RiskCode=N'Low'
                    AND BeforeJson IS NOT NULL));
            IF @requestId IS NULL OR @locationId IS NULL OR @locationId<=0
               OR @team IS NULL OR @team<=0 OR @requester IS NULL
               OR @requester=@reviewer OR @expected IS NULL
                THROW 51601,N'OPTIONC_CREATE_LOW_REQUEST_DENIED',1;

            SELECT @leader=ReviewerUserId,@leaderKey=DecisionKey
            FROM dbo.ChangeRequestLeaderReviews WITH(UPDLOCK,HOLDLOCK)
            WHERE ChangeRequestId=@requestId AND ReviewStage=N'LeaderFirstLevel'
              AND Status=N'Approved' AND ReviewerUserId<>@requester
              AND ReviewerUserId IS NOT NULL AND DecisionKey IS NOT NULL;
            IF @leader IS NULL OR @leaderKey IS NULL
                THROW 51602,N'OPTIONC_CREATE_LOW_LEADER_REQUIRED',1;
            IF @operation=N'CreateLocation'
               AND (@adminEligible<>1 OR @reviewer=@leader)
                THROW 51603,N'OPTIONC_CREATE_INDEPENDENT_ADMIN_REQUIRED',1;
            IF @operation=N'UpdatePublishedLocation' AND @reviewer<>@leader
                THROW 51604,N'OPTIONC_LOW_FINAL_REVIEWER_MUST_BE_LEADER',1;

            SELECT @locationVersion=RowVersion,@status=ApprovalStatus,
                   @active=IsActive,@sourceOwner=CreatedByUserId
            FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK)
            WHERE LocationId=@locationId AND OrganizationId=@org
              AND TeamId=@team AND LocationType=N'Customer';
            IF @locationVersion IS NULL OR @locationVersion<>@expected
               OR @sourceOwner<>@requester
                THROW 51605,N'OPTIONC_CREATE_LOW_SOURCE_DRIFT',1;
            IF (@operation=N'CreateLocation' AND (@active<>0 OR @status<>N'Pending'))
               OR (@operation=N'UpdatePublishedLocation'
                   AND (@active<>1 OR @status<>N'Approved'))
                THROW 51606,N'OPTIONC_CREATE_LOW_SOURCE_STATE_INVALID',1;

            IF @operation=N'CreateLocation'
            BEGIN
                SELECT @reservationVersion=ReservedLocationRowVersion
                FROM dbo.LocationCreateReservations WITH(UPDLOCK,HOLDLOCK)
                WHERE ChangeRequestId=@requestId AND LocationId=@locationId
                  AND OrganizationId=@org AND TeamId=@team
                  AND ReservationStatus=N'Pending';
                IF @reservationVersion IS NULL OR @reservationVersion<>@expected
                    THROW 51607,N'OPTIONC_CREATE_RESERVATION_DRIFT',1;
            END

            SELECT @stageHash=ProposedSha256,@stageVersion=RowVersion,
                @geocode=GeocodeStatus,@duplicate=DuplicateReviewStatus,
                @latitude=Latitude,@longitude=Longitude
            FROM dbo.LocationPublicationStages WITH(UPDLOCK,HOLDLOCK)
            WHERE ChangeRequestId=@requestId AND LocationId=@locationId
              AND OrganizationId=@org AND TeamId=@team
              AND ExpectedLocationRowVersion=@expected
              AND ProposedJson=@proposed AND StageStatus=N'ReadyForPublish'
              AND DuplicateReviewStatus=N'Cleared'
              AND DuplicateEvidenceSha256=ProposedSha256
              AND (GeocodeStatus=N'Succeeded' OR
                   (@operation=N'UpdatePublishedLocation'
                    AND GeocodeStatus=N'NotRequired'));
            IF @stageVersion IS NULL OR @stageHash IS NULL
               OR @stageHash<>HASHBYTES('SHA2_256',CONVERT(varbinary(max),@proposed))
               OR ISJSON(@proposed)<>1
               OR NULLIF(LTRIM(RTRIM(JSON_VALUE(@proposed,N'$.LocationName'))),N'') IS NULL
               OR NULLIF(LTRIM(RTRIM(JSON_VALUE(@proposed,N'$.Address'))),N'') IS NULL
                THROW 51608,N'OPTIONC_CREATE_LOW_STAGE_INVALID',1;
            IF @operation=N'CreateLocation' AND
               (@geocode<>N'Succeeded' OR @latitude IS NULL OR @longitude IS NULL)
                THROW 51609,N'OPTIONC_CREATE_GEOCODE_REQUIRED',1;

            IF EXISTS(SELECT 1 FROM dbo.ChangeRequestEvents WITH(UPDLOCK,HOLDLOCK)
                      WHERE DecisionKey=@decision)
               OR EXISTS(SELECT 1 FROM dbo.ChangeRequestLeaderReviews WITH(UPDLOCK,HOLDLOCK)
                         WHERE DecisionKey=@decision)
                THROW 51610,N'OPTIONC_CREATE_LOW_DECISION_REPLAY',1;

            SELECT @currentVersion=VersionNumber
            FROM dbo.LocationPublishedVersions WITH(UPDLOCK,HOLDLOCK)
            WHERE LocationId=@locationId AND IsCurrent=1;
            IF (@operation=N'CreateLocation' AND @currentVersion IS NOT NULL)
               OR (@operation=N'UpdatePublishedLocation' AND @currentVersion IS NULL)
                THROW 51611,N'OPTIONC_CREATE_LOW_LEDGER_DRIFT',1;

            IF @operation=N'UpdatePublishedLocation'
            BEGIN
                UPDATE dbo.LocationPublishedVersions SET IsCurrent=0
                WHERE LocationId=@locationId AND VersionNumber=@currentVersion
                  AND IsCurrent=1;
                IF @@ROWCOUNT<>1 THROW 51612,N'OPTIONC_LOW_BASELINE_CONFLICT',1;
            END

            UPDATE dbo.Locations SET
                LocationName=JSON_VALUE(@proposed,N'$.LocationName'),
                City=JSON_VALUE(@proposed,N'$.City'),
                District=JSON_VALUE(@proposed,N'$.District'),
                Address=JSON_VALUE(@proposed,N'$.Address'),
                PlusCode=JSON_VALUE(@proposed,N'$.PlusCode'),
                TaxId=JSON_VALUE(@proposed,N'$.TaxId'),
                MasterNote=JSON_VALUE(@proposed,N'$.MasterNote'),
                ApprovalStatus=N'Approved',IsActive=1,
                Latitude=CASE WHEN @operation=N'CreateLocation' THEN @latitude ELSE Latitude END,
                Longitude=CASE WHEN @operation=N'CreateLocation' THEN @longitude ELSE Longitude END,
                UpdatedAt=@now
            WHERE LocationId=@locationId AND OrganizationId=@org AND TeamId=@team
              AND CreatedByUserId=@requester AND RowVersion=@locationVersion
              AND ((@operation=N'CreateLocation' AND ApprovalStatus=N'Pending' AND IsActive=0)
                OR (@operation=N'UpdatePublishedLocation'
                    AND ApprovalStatus=N'Approved' AND IsActive=1));
            IF @@ROWCOUNT<>1 THROW 51613,N'OPTIONC_CREATE_LOW_LOCATION_CONFLICT',1;

            INSERT dbo.LocationPublishedVersions(
                LocationId,VersionNumber,ChangeRequestId,IsCurrent,
                PublishedJson,PublishedSha256,PublishedAt,
                PublishedByUserId,SourceLocationRowVersion)
            VALUES(@locationId,CASE WHEN @operation=N'CreateLocation' THEN 1 ELSE @currentVersion+1 END,
                @requestId,1,@proposed,@stageHash,@now,@reviewer,@locationVersion);

            IF @operation=N'CreateLocation'
            BEGIN
                UPDATE dbo.LocationCreateReservations SET
                    ReservationStatus=N'Published',UpdatedAt=@now
                WHERE ChangeRequestId=@requestId AND LocationId=@locationId
                  AND ReservationStatus=N'Pending'
                  AND ReservedLocationRowVersion=@locationVersion;
                IF @@ROWCOUNT<>1 THROW 51614,N'OPTIONC_CREATE_RESERVATION_CONFLICT',1;
            END

            UPDATE dbo.LocationPublicationStages SET
                StageStatus=N'Published',UpdatedAt=@now
            WHERE ChangeRequestId=@requestId AND RowVersion=@stageVersion
              AND StageStatus=N'ReadyForPublish';
            IF @@ROWCOUNT<>1 THROW 51615,N'OPTIONC_CREATE_LOW_STAGE_CONFLICT',1;

            UPDATE dbo.ChangeRequests SET Status=N'Applied',
                ReviewedByUserId=@reviewer,ReviewedAt=@now,
                AppliedAt=@now,ReviewReason=@reason
            WHERE ChangeRequestId=@requestId AND RowVersion=@requestVersion
              AND Status=N'Pending' AND RequestedByUserId<>@reviewer;
            IF @@ROWCOUNT<>1 THROW 51616,N'OPTIONC_CREATE_LOW_REQUEST_CONFLICT',1;

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
        cmd.Parameters.AddWithValue("@reviewer", finalReviewerUserId);
        cmd.Parameters.AddWithValue("@decision", decisionKey);
        cmd.Parameters.AddWithValue("@reason", reason);
        cmd.Parameters.AddWithValue("@adminEligible", independentAdminForCreate ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
