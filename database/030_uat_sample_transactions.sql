-- 可選：建立擬真交易資料；可重複執行。
SET NOCOUNT ON; SET XACT_ABORT ON;
BEGIN TRY BEGIN TRANSACTION;
DECLARE @Now datetime2=SYSUTCDATETIME(),@Visitor int,@Leader int,@Org int,@Team int,@HQ int,@A int,@B int,@Rate int;
SELECT @Visitor=UserId,@Org=OrganizationId,@Team=TeamId FROM dbo.Users WHERE EmployeeNo=N'visitor01';
SELECT @Leader=UserId FROM dbo.Users WHERE EmployeeNo=N'leader01';
SELECT @HQ=LocationId FROM dbo.Locations WHERE OrganizationId=@Org AND LocationCode=N'HQ';
SELECT @A=LocationId FROM dbo.Locations WHERE OrganizationId=@Org AND LocationCode=N'CUST-A';
SELECT @B=LocationId FROM dbo.Locations WHERE OrganizationId=@Org AND LocationCode=N'CUST-B';
SELECT TOP 1 @Rate=MileageRateRuleId FROM dbo.MileageRateRules WHERE IsActive=1 AND (OrganizationId=@Org OR OrganizationId IS NULL) AND VehicleType=N'Motorcycle' ORDER BY EffectiveFrom DESC;
IF @Visitor IS NULL OR @Leader IS NULL OR @HQ IS NULL OR @A IS NULL THROW 51000,N'請先執行 020_uat_demo_seed.sql',1;

DECLARE @Trip bigint;
IF NOT EXISTS(SELECT 1 FROM dbo.VisitTrips WHERE TripNo=N'UAT20260801A')
BEGIN
 INSERT dbo.VisitTrips(TripNo,UserId,OrganizationId,TeamId,VisitDate,StartTime,EndTime,HasTimeOverlapWarning,TimeOverlapConfirmed,Status,VehicleType,Purpose,Notes,SubmittedAt,ApprovedAt,CreatedAt,CreatedByUserId,UpdatedAt,UpdatedByUserId,ReturnReason)
 VALUES(N'UAT20260801A',@Visitor,@Org,@Team,'2026-08-01','08:30','17:00',0,0,N'Approved',N'Motorcycle',N'定期外訪',N'UAT 已核准範例','2026-08-01T17:05:00','2026-08-02T09:00:00','2026-08-01T08:20:00',@Visitor,'2026-08-02T09:00:00',@Leader,NULL);
 SET @Trip=CONVERT(bigint,SCOPE_IDENTITY());
 INSERT dbo.VisitTripStops(VisitTripId,StopSequence,LocationId,ProjectId,VisitTypeId,LocationNameSnapshot,AddressSnapshot,VisitPurpose,Notes,CreatedAt)
 SELECT @Trip,1,@HQ,NULL,NULL,LocationName,Address,N'出發',NULL,@Now FROM dbo.Locations WHERE LocationId=@HQ;
 INSERT dbo.VisitTripStops(VisitTripId,StopSequence,LocationId,ProjectId,VisitTypeId,LocationNameSnapshot,AddressSnapshot,VisitPurpose,Notes,CreatedAt)
 SELECT @Trip,2,@A,NULL,NULL,LocationName,Address,N'親訪',NULL,@Now FROM dbo.Locations WHERE LocationId=@A;
 INSERT dbo.VisitTripStops(VisitTripId,StopSequence,LocationId,ProjectId,VisitTypeId,LocationNameSnapshot,AddressSnapshot,VisitPurpose,Notes,CreatedAt)
 SELECT @Trip,3,@B,NULL,NULL,LocationName,Address,N'親訪',NULL,@Now FROM dbo.Locations WHERE LocationId=@B;
 INSERT dbo.MileageCalculations(VisitTripId,MileageRateRuleId,SystemDistanceKm,ClaimedDistanceKm,ApprovedDistanceKm,RatePerKmSnapshot,ClaimedAmount,ApprovedAmount,CalculationSource,CalculatedAt,CreatedAt)
 VALUES(@Trip,@Rate,38.60,40.00,38.60,2.50,100.00,96.50,N'UAT sample','2026-08-02T08:50:00',@Now);
 INSERT dbo.ApprovalRecords(VisitTripId,ApprovalStep,ApproverUserId,Action,Comments,ActionAt) VALUES(@Trip,1,@Leader,N'Approved',N'UAT sample approval','2026-08-02T09:00:00');
 INSERT dbo.VisitTripStatusHistory(VisitTripId,PreviousStatus,NewStatus,Action,ActionByUserId,Comments,ActionAt) VALUES(@Trip,N'PendingApproval',N'Approved',N'Approve',@Leader,N'UAT sample', '2026-08-02T09:00:00');
END;

IF NOT EXISTS(SELECT 1 FROM dbo.VisitTrips WHERE TripNo=N'UAT20260803P')
BEGIN
 INSERT dbo.VisitTrips(TripNo,UserId,OrganizationId,TeamId,VisitDate,StartTime,EndTime,HasTimeOverlapWarning,TimeOverlapConfirmed,Status,VehicleType,Purpose,Notes,SubmittedAt,CreatedAt,CreatedByUserId,UpdatedAt,UpdatedByUserId)
 VALUES(N'UAT20260803P',@Visitor,@Org,@Team,'2026-08-03','09:00','16:00',0,0,N'Submitted',N'Motorcycle',N'專案外訪',N'等待小組長批次里程','2026-08-03T16:05:00','2026-08-03T08:50:00',@Visitor,'2026-08-03T16:05:00',@Visitor);
 SET @Trip=CONVERT(bigint,SCOPE_IDENTITY());
 INSERT dbo.VisitTripStops(VisitTripId,StopSequence,LocationId,LocationNameSnapshot,AddressSnapshot,CreatedAt) SELECT @Trip,1,@HQ,LocationName,Address,@Now FROM dbo.Locations WHERE LocationId=@HQ;
 INSERT dbo.VisitTripStops(VisitTripId,StopSequence,LocationId,LocationNameSnapshot,AddressSnapshot,CreatedAt) SELECT @Trip,2,@A,LocationName,Address,@Now FROM dbo.Locations WHERE LocationId=@A;
 INSERT dbo.MileageCalculations(VisitTripId,ClaimedDistanceKm,CreatedAt) VALUES(@Trip,31.20,@Now);
 INSERT dbo.VisitTripStatusHistory(VisitTripId,PreviousStatus,NewStatus,Action,ActionByUserId,ActionAt) VALUES(@Trip,N'Draft',N'Submitted',N'Submit',@Visitor,'2026-08-03T16:05:00');
END;
COMMIT; PRINT N'030_uat_sample_transactions.sql completed.';
END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
