using System.Data;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Candidate-only atomic note writer. Authorization is deliberately supplied
/// by the live adapter inside the caller's serializable transaction. This is
/// not a legacy direct write: request, version, immutable event and AuditLog
/// evidence are committed with the Location update or all roll back.
/// </summary>
internal static class V180OptionCLowRiskDirectWriter
{
    internal static async Task<Guid> ExecuteAsync(
        SqlConnection connection, SqlTransaction transaction, int locationId,
        int organizationId, int teamId, int actorUserId, byte[] expectedRowVersion,
        string beforeJson, string proposedJson, string? masterNote, string reason,
        Guid operationKey, bool transactionBoundLiveAuthorization,
        CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open
            || transaction.Connection != connection
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || locationId <= 0 || organizationId <= 0 || teamId <= 0
            || actorUserId <= 0 || expectedRowVersion.Length != 8
            || operationKey == Guid.Empty || !transactionBoundLiveAuthorization
            || string.IsNullOrWhiteSpace(reason) || reason != reason.Trim()
            || reason.Length > 1000 || masterNote?.Length > 1000)
            throw new InvalidOperationException("OPTIONC_LOW_DIRECT_INPUT_DENIED");
        V180OptionCExecutionPolicy.RequireExecutionTarget(connection);

        const string sql = """
            SET XACT_ABORT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions
                WHERE VersionNumber=N'1.8.0-LOWRISK-DIRECT')
                THROW 51710,N'OPTIONC_LOW_DIRECT_SCHEMA_NOT_READY',1;
            IF EXISTS(SELECT 1 FROM dbo.ChangeRequestEvents WITH(UPDLOCK,HOLDLOCK)
                      WHERE DecisionKey=@operationKey)
                THROW 51711,N'OPTIONC_LOW_DIRECT_REPLAY',1;

            DECLARE @actual binary(8),@owner int,@currentVersion int,
                    @requestId bigint,@requestPublicId uniqueidentifier=NEWID(),
                    @now datetime2(3)=SYSUTCDATETIME(),@correlation uniqueidentifier=NEWID(),
                    @hash binary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(max),@proposed));
            SELECT @actual=RowVersion,@owner=CreatedByUserId
            FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK)
            WHERE LocationId=@locationId AND OrganizationId=@org AND TeamId=@team
              AND LocationType=N'Customer' AND ApprovalStatus=N'Approved' AND IsActive=1;
            IF @actual IS NULL THROW 51712,N'OPTIONC_LOW_DIRECT_SOURCE_NOT_PUBLISHED',1;
            IF @actual<>@expected THROW 51713,N'ROWVERSION_CONFLICT',1;
            IF EXISTS(SELECT 1 FROM dbo.ChangeRequests WITH(UPDLOCK,HOLDLOCK)
                WHERE OrganizationId=@org AND EntityKind=N'Location'
                  AND EntityId=CONVERT(nvarchar(80),@locationId) AND Status=N'Pending')
                THROW 51714,N'OPTIONC_LOW_DIRECT_PENDING_CONFLICT',1;
            IF ISJSON(@before)<>1 OR ISJSON(@proposed)<>1
               OR ISNULL(JSON_VALUE(@before,N'$.LocationName'),N'')
                    <>ISNULL(JSON_VALUE(@proposed,N'$.LocationName'),N'')
               OR ISNULL(JSON_VALUE(@before,N'$.City'),N'')
                    <>ISNULL(JSON_VALUE(@proposed,N'$.City'),N'')
               OR ISNULL(JSON_VALUE(@before,N'$.District'),N'')
                    <>ISNULL(JSON_VALUE(@proposed,N'$.District'),N'')
               OR ISNULL(JSON_VALUE(@before,N'$.Address'),N'')
                    <>ISNULL(JSON_VALUE(@proposed,N'$.Address'),N'')
               OR ISNULL(JSON_VALUE(@before,N'$.PlusCode'),N'')
                    <>ISNULL(JSON_VALUE(@proposed,N'$.PlusCode'),N'')
               OR ISNULL(JSON_VALUE(@before,N'$.TaxId'),N'')
                    <>ISNULL(JSON_VALUE(@proposed,N'$.TaxId'),N'')
                THROW 51715,N'OPTIONC_LOW_DIRECT_HIGH_RISK_DENIED',1;
            IF ISNULL(JSON_VALUE(@before,N'$.MasterNote'),N'')
                =ISNULL(JSON_VALUE(@proposed,N'$.MasterNote'),N'')
                THROW 51716,N'OPTIONC_LOW_DIRECT_NO_CHANGE',1;

            SELECT @currentVersion=VersionNumber
            FROM dbo.LocationPublishedVersions WITH(UPDLOCK,HOLDLOCK)
            WHERE LocationId=@locationId AND IsCurrent=1;
            IF @currentVersion IS NULL
                THROW 51717,N'OPTIONC_LOW_DIRECT_LEDGER_MISSING',1;

            INSERT dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,
                EntityKind,EntityId,OperationCode,RiskCode,ExpectedEntityRowVersion,
                BeforeJson,ProposedJson,EvidenceJson,RequestedByUserId,SubmittedAt,
                Status,ReviewReason,AppliedAt)
            VALUES(@requestPublicId,@org,@team,N'Location',CONVERT(nvarchar(80),@locationId),
                N'UpdatePublishedLocation',N'Low',@expected,@before,@proposed,
                N'{"PublicationMode":"DirectLowRisk","AuthorizationSource":"LiveHR"}',
                @actor,@now,N'Applied',@reason,@now);
            SET @requestId=SCOPE_IDENTITY();

            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,
                OccurredAt,CorrelationId,DecisionKey,DetailsJson)
            VALUES(@requestId,N'Submitted',@actor,@now,@correlation,NULL,
                    N'{"PublicationMode":"DirectLowRisk"}'),
                  (@requestId,N'DirectLowRiskApplied',@actor,@now,@correlation,
                    @operationKey,N'{"AuthorizationSource":"LiveHR"}');

            UPDATE dbo.LocationPublishedVersions SET IsCurrent=0
            WHERE LocationId=@locationId AND VersionNumber=@currentVersion AND IsCurrent=1;
            IF @@ROWCOUNT<>1 THROW 51718,N'OPTIONC_LOW_DIRECT_VERSION_CONFLICT',1;

            UPDATE dbo.Locations SET MasterNote=@note,UpdatedAt=@now
            WHERE LocationId=@locationId AND OrganizationId=@org AND TeamId=@team
              AND RowVersion=@actual AND ApprovalStatus=N'Approved' AND IsActive=1;
            IF @@ROWCOUNT<>1 THROW 51719,N'OPTIONC_LOW_DIRECT_LOCATION_CONFLICT',1;

            INSERT dbo.LocationPublishedVersions(LocationId,VersionNumber,
                ChangeRequestId,IsCurrent,PublishedJson,PublishedSha256,PublishedAt,
                PublishedByUserId,SourceLocationRowVersion)
            VALUES(@locationId,@currentVersion+1,@requestId,1,@proposed,@hash,@now,@actor,@actual);

            INSERT dbo.AuditLogs(UserId,EntityType,EntityId,Action,OldValues,
                NewValues,CorrelationId,CreatedAt)
            VALUES(@actor,N'Location',CONVERT(nvarchar(80),@locationId),
                N'LocationLowRiskDirectApplied',@before,@proposed,@correlation,@now);
            SELECT @requestPublicId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction)
        { CommandTimeout = 30 };
        command.Parameters.AddWithValue("@locationId", locationId);
        command.Parameters.AddWithValue("@org", organizationId);
        command.Parameters.AddWithValue("@team", teamId);
        command.Parameters.AddWithValue("@actor", actorUserId);
        command.Parameters.AddWithValue("@expected", expectedRowVersion);
        command.Parameters.AddWithValue("@before", beforeJson);
        command.Parameters.AddWithValue("@proposed", proposedJson);
        command.Parameters.AddWithValue("@note", (object?)masterNote ?? DBNull.Value);
        command.Parameters.AddWithValue("@reason", reason);
        command.Parameters.AddWithValue("@operationKey", operationKey);
        return (Guid)(await command.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("OPTIONC_LOW_DIRECT_RESULT_MISSING"));
    }
}
