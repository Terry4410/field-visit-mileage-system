export interface TeamScope{teamId:number;teamName:string;isPrimary:boolean}
export interface CurrentUser{userId:number;employeeNo:string;displayName:string;email?:string;organizationId?:number;teamId?:number;teamName?:string;roles:string[];teamScopes?:TeamScope[]}
export interface LoginResponse{accessToken:string;expiresAtUtc:string;user:CurrentUser}
export interface TripStopInput{locationId?:number;projectId?:number;visitTypeId?:number;sourceType:string;locationName:string;address?:string;visitPurpose?:string;notes?:string}
export interface Trip{visitTripId:number;tripNo:string;userId:number;visitorName:string;teamId?:number;teamName?:string;visitDate:string;startTime?:string;endTime?:string;hasTimeOverlapWarning:boolean;timeOverlapConfirmed:boolean;status:string;statusName:string;purpose?:string;notes?:string;returnReason?:string;claimedDistanceKm?:number;systemDistanceKm?:number;approvedDistanceKm?:number;ratePerKmSnapshot?:number;approvedAmount?:number;stops:TripStopInput[];rowVersion:string}
export interface Location{locationId:number;teamId?:number;locationName:string;locationType:string;city?:string;district?:string;address?:string;plusCode?:string;latitude?:number;longitude?:number;isTemporary:boolean;approvalStatus:string;geocodingStatus:string;isActive:boolean;createdAt:string;rowVersion:string}

export interface SmartLocationItem{
  locationId:number;
  locationCode?:string|null;
  locationName:string;
  locationType:string;
  city?:string|null;
  district?:string|null;
  address?:string|null;
  plusCode?:string|null;
  latitude?:number|null;
  longitude?:number|null;
}

export interface LocationSearchItem extends SmartLocationItem{}

export interface LocationSearchResult{
  items:LocationSearchItem[];
  page:number;
  pageSize:number;
  totalCount:number;
  hasNextPage:boolean;
}

export interface LocationFavoriteItem extends SmartLocationItem{
  sortOrder:number;
  createdAt:string;
}

export interface LocationRecentItem extends SmartLocationItem{
  lastVisitedOn:string;
}

export interface LocationNearbyItem extends SmartLocationItem{
  latitude:number;
  longitude:number;
  distanceKm:number;
}
export interface Team{teamId:number;organizationId:number;teamCode:string;teamName:string}
export interface Project{projectId:number;teamId?:number;projectCode:string;projectName:string;description?:string;locationMode:string;startDate?:string;endDate?:string;isActive:boolean}
export interface VisitType{visitTypeId:number;visitTypeCode:string;visitTypeName:string;description?:string;sortOrder:number;isActive:boolean}
export interface MileageRate{mileageRateRuleId:number;organizationId?:number;ruleName:string;vehicleType:string;ratePerKm:number;effectiveFrom:string;effectiveTo?:string;isActive:boolean}
export interface MileageReport{tripNo:string;visitDate:string;visitorName:string;teamName?:string;route:string;claimedDistanceKm?:number;systemDistanceKm?:number;approvedDistanceKm?:number;ratePerKmSnapshot?:number;approvedAmount?:number;status:string;statusName:string}

export interface QueryStop{stopSequence:number;locationId?:number;locationCode?:string;locationName:string;address?:string;projectId?:number;projectCode?:string;projectName?:string;visitTypeId?:number;visitTypeCode?:string;visitTypeName?:string;visitPurpose?:string;notes?:string}
export interface TripQueryRow{visitTripId:number;tripNo:string;visitDate:string;startTime?:string;endTime?:string;visitorId:number;employeeNo:string;visitorName:string;teamId?:number;teamName?:string;route:string;projectNames:string;visitTypeNames:string;claimedDistanceKm?:number;systemDistanceKm?:number;approvedDistanceKm?:number;ratePerKmSnapshot?:number;subsidyAmount?:number;mileageState:string;status:string;statusName:string;snapshotVersion:number;isSnapshot:boolean;notes?:string;returnReason?:string;correctionStatus?:string;stops:QueryStop[]}
export interface PagedResult<T>{items:T[];page:number;pageSize:number;totalCount:number;totalPages:number}
export interface UserOption{userId:number;employeeNo:string;displayName:string;teamId?:number;teamName?:string}

export interface CorrectionStopProposal{stopSequence:number;locationCode?:string;locationName:string;address?:string;projectCode?:string;projectName?:string;visitTypeCode?:string;visitTypeName?:string;visitPurpose?:string;notes?:string}
export interface CorrectionProposal{visitDate:string;startTime?:string;endTime?:string;notes?:string;claimedDistanceKm?:number;approvedDistanceKm?:number;ratePerKm?:number;subsidyAmount?:number;stops:CorrectionStopProposal[]}
export interface CorrectionDraft{visitTripId:number;tripNo:string;baseSnapshotVersion:number;proposal:CorrectionProposal}
export interface CorrectionChange{fieldName:string;oldValue?:string;newValue?:string}
export interface CorrectionRequest{correctionRequestId:number;visitTripId:number;tripNo:string;visitorName:string;teamName?:string;baseSnapshotVersion:number;resultSnapshotVersion?:number;status:string;reason:string;requestedAt:string;requestedBy:string;leaderReviewedAt?:string;leaderReviewedBy?:string;leaderComments?:string;adminClosedAt?:string;adminClosedBy?:string;adminComments?:string;requiresAdminClose:boolean;proposal:CorrectionProposal;changes:CorrectionChange[];rowVersion:string}

export interface AdminUserAccess{userId:number;employeeNo:string;displayName:string;email?:string;isActive:boolean;roles:string[];teamScopes:TeamScope[]}
export interface ManagedTeam{teamId:number;organizationId:number;teamCode:string;teamName:string;isActive:boolean}
export interface ManagedLocation{locationId:number;locationCode:string;teamId?:number;teamName?:string;locationName:string;locationType:string;city?:string;district?:string;address?:string;plusCode?:string;latitude?:number;longitude?:number;isTemporary:boolean;approvalStatus:string;geocodingStatus:string;isActive:boolean;createdAt:string;rowVersion:string}
export interface ImportPreviewItem{rowNumber:number;entityType:string;action:string;status:string;displayKey:string;errorMessage?:string}
export interface ImportPreview{importBatchId:string;importType:string;totalCount:number;validCount:number;errorCount:number;items:ImportPreviewItem[]}
export interface ImportConfirmResult{importBatchId:string;created:number;updated:number;unchanged:number;failed:number;errors:string[]}
export interface PeopleBulkPreviewItem{
  rowNumber:number;
  sheet:string;
  entityType:string;
  action:string;
  displayKey:string;
  status:string;
  message?:string;
  isRetroactive:boolean;
}

export interface PeopleBulkPreview{
  importBatchId:string;
  totalCount:number;
  validCount:number;
  errorCount:number;
  requiresRetroactiveConfirmation:boolean;
  items:PeopleBulkPreviewItem[];
}

export interface PeopleBulkConfirmResult{
  importBatchId:string;
  created:number;
  updated:number;
  unchanged:number;
  failed:number;
  errors:string[];
}

export interface BackgroundJob{backgroundJobId:string;jobType:string;status:string;mode?:string;totalCount:number;successCount:number;failedCount:number;skippedCount:number;errorMessage?:string;createdAt:string;startedAt?:string;completedAt?:string}
export interface DashboardSummary{thisMonthTrips:number;pendingApproval:number;approved:number;pendingLocations:number;pendingCorrections:number;currentRatePerKm?:number}


export interface ProjectLocationAdminItem{
  locationId:number;
  locationCode?:string|null;
  locationName:string;
  city?:string|null;
  district?:string|null;
  address?:string|null;
  plusCode?:string|null;
}

export interface ProjectLocationCount{
  projectId:number;
  count:number;
}

export interface ProjectLocationCandidateResult{
  items:ProjectLocationAdminItem[];
  page:number;
  pageSize:number;
  totalCount:number;
  hasNextPage:boolean;
}
