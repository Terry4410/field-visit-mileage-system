/* OPTION C MERGE DETAILS — DISPOSABLE CANDIDATE ONLY.
   Additive detail table, not an approval ledger. Never execute on UAT/Production.
   013 remains reserved. The canonical ChangeRequests row remains the authority. */
SET NOCOUNT ON;
IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 51460, N'OPTIONC_MERGE_DISPOSABLE_ONLY', 1;
IF OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NULL
    THROW 51461, N'OPTIONC_MERGE_REQUIRES_011', 1;
IF OBJECT_ID(N'dbo.LocationPublicationStages',N'U') IS NULL
    THROW 51462, N'OPTIONC_MERGE_REQUIRES_014', 1;
IF OBJECT_ID(N'dbo.LocationMergeDetails',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocationMergeDetails
    (
        ChangeRequestId bigint NOT NULL CONSTRAINT PK_OptionC_MergeDetails PRIMARY KEY,
        SourceLocationId int NOT NULL,
        SurvivorLocationId int NOT NULL,
        SourceOrganizationId int NOT NULL,
        SurvivorOrganizationId int NOT NULL,
        SourceTeamId int NOT NULL,
        SurvivorTeamId int NOT NULL,
        SourceLocationRowVersion binary(8) NOT NULL,
        SurvivorLocationRowVersion binary(8) NOT NULL,
        BeforeJson nvarchar(max) NOT NULL,
        ProposedJson nvarchar(max) NOT NULL,
        EvidenceJson nvarchar(max) NULL,
        CONSTRAINT FK_OptionC_MergeDetails_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId),
        CONSTRAINT CK_OptionC_MergeDetails_Different CHECK(SourceLocationId<>SurvivorLocationId),
        CONSTRAINT CK_OptionC_MergeDetails_SameOrg CHECK(SourceOrganizationId=SurvivorOrganizationId),
        CONSTRAINT CK_OptionC_MergeDetails_Json CHECK(ISJSON(BeforeJson)=1 AND ISJSON(ProposedJson)=1)
    );
END;
