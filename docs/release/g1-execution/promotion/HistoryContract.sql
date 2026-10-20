-- History data stay in SQL memory; return digest only to controlled callers.
SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),
 (SELECT * FROM dbo.VisitTrips ORDER BY VisitTripId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.VisitTripStops ORDER BY VisitTripStopId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.VisitTripSnapshots ORDER BY VisitTripSnapshotId FOR JSON PATH,INCLUDE_NULL_VALUES)+
 (SELECT * FROM dbo.VisitTripSnapshotStops ORDER BY VisitTripSnapshotStopId FOR JSON PATH,INCLUDE_NULL_VALUES))),2);
