export interface TeamScope{teamId:number;teamName:string;isPrimary:boolean}
export interface DataScope{scopeType:string;organizationId?:number;teamId?:number;teamName?:string}
export interface CurrentUser{userId:number;employeeNo:string;displayName:string;email?:string;organizationId?:number;teamId?:number;teamName?:string;roles:string[];teamScopes?:TeamScope[];dataScopes?:DataScope[]}
export interface LoginResponse{accessToken:string;expiresAtUtc:string;user:CurrentUser}
export interface TripStopInput{locationId?:number;projectId?:number;visitTypeId?:number;sourceType:string;locationName:string;address?:string;visitPurpose?:string;notes?:string}
export interface Trip{visitTripId:number;tripNo:string;userId:number;visitorName:string;teamId?:number;teamName?:string;visitDate:string;startTime?:string;endTime?:string;hasTimeOverlapWarning:boolean;timeOverlapConfirmed:boolean;status:string;statusName:string;purpose?:string;notes?:string;returnReason?:string;claimedDistanceKm?:number;systemDistanceKm?:number;approvedDistanceKm?:number;ratePerKmSnapshot?:number;approvedAmount?:number;startDeploymentSiteId?:number;startDeploymentSiteCode?:string;startDeploymentSiteName?:string;startDeploymentAddress?:string;endDeploymentSiteId?:number;endDeploymentSiteCode?:string;endDeploymentSiteName?:string;endDeploymentAddress?:string;stops:TripStopInput[];rowVersion:string;vehicleType?:string;routeCalculationAttemptId?:number;mileageSource?:string}
export interface Location{locationId:number;teamId?:number;locationName:string;locationType:string;city?:string;district?:string;address?:string;plusCode?:string;latitude?:number;longitude?:number;isTemporary:boolean;approvalStatus:string;geocodingStatus:string;isActive:boolean;createdAt:string;rowVersion:string}
export interface TripContextDeploymentSite{deploymentSiteId:number;centerId:number;centerCode:string;centerName:string;siteCode:string;siteName:string;locationId:number;locationCode?:string|null;locationName:string;address?:string|null;isPrimary:boolean}
export interface TripContext{employmentId:number;visitDate:string;eligibleForTrip:boolean;validationCode:string;validationMessage:string;selectedTeamId?:number|null;eligibleDeploymentSites:TripContextDeploymentSite[];primaryDeploymentSiteId?:number|null;defaultStartDeploymentSiteId?:number|null;defaultEndDeploymentSiteId?:number|null;officialDeploymentSites?:TripContextDeploymentSite[]|null}
export interface RoutePreviewResult{routeCalculationAttemptId:number;correlationId:string;status:string;suggestedDistanceKm?:number|null;durationSeconds?:number|null;encodedPolyline?:string|null;errorCode?:string|null;errorMessage?:string|null}

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

export interface LocationNoteEntry{
  historyId:number;
  teamId:number;
  teamName:string;
  note:string;
  action:string;
  changeReason?:string;
  changedAt:string;
  changedByUserId:number;
  changedBy:string;
  sourceLocationId?:number;
  sourceLocationName?:string;
}
export interface LocationAudit{
  auditLogId:number;
  action:string;
  oldValues?:string;
  newValues?:string;
  changedAt:string;
  changedByUserId?:number;
  changedBy?:string;
  sourceLocationId?:number;
  sourceLocationName?:string;
}
export interface LocationOfficialSite{
  locationId:number;
  locationCode:string;
  locationName:string;
  isOfficialSite:boolean;
  deploymentSiteId?:number;
  siteCode?:string;
  siteName?:string;
  centerCode?:string;
  centerName?:string;
  effectiveFrom?:string;
  effectiveTo?:string;
  isActive?:boolean;
}
export interface MasterDataRow{
  id:number;
  key:string;
  parentKey?:string|null;
  detail?:string|null;
  effectiveFrom:string;
  effectiveTo?:string|null;
  isActive?:boolean|null;
  isPrimary?:boolean|null;
  rowVersion?:string|null;
  referenceKey?:string|null;
}
export interface LocationMaintenance{
  locationId:number;
  locationCode?:string;
  locationName:string;
  locationType:string;
  teamId?:number;
  teamName?:string;
  city?:string;
  district?:string;
  address?:string;
  plusCode?:string;
  taxId?:string;
  masterNote?:string;
  isActive:boolean;
  duplicateOfLocationId?:number;
  duplicateReason?:string;
  notes:LocationNoteEntry[];
  addressAudit:LocationAudit[];
  rowVersion:string;
}
export interface LocationDuplicateCandidate{
  locationId:number;
  locationCode?:string;
  locationName:string;
  address?:string;
  plusCode?:string;
  taxId?:string;
  matchReasons:string[];
}
export interface LocationMergeMaster{
  locationId:number;
  locationCode?:string|null;
  locationName:string;
  locationType:string;
  teamId?:number|null;
  teamName?:string|null;
  city?:string|null;
  district?:string|null;
  address?:string|null;
  plusCode?:string|null;
  taxId?:string|null;
  masterNote?:string|null;
  rowVersion:string;
}
export interface LocationMergeMasterSelection{
  locationName:string;
  locationType:string;
  teamId:number|null;
  city:string|null;
  district:string|null;
  address:string|null;
  plusCode:string|null;
  taxId:string|null;
  masterNote:string|null;
}
export interface LocationMergePreview{
  sourceLocationId:number;
  survivorLocationId:number;
  canMerge:boolean;
  blockingReason?:string;
  tripReferenceCount:number;
  projectReferenceCount:number;
  favoriteReferenceCount:number;
  noteHistoryCount:number;
  currentDeploymentSiteReferenceCount:number;
  snapshotReferenceCount:number;
  governmentMatchCount:number;
  source?:LocationMergeMaster|null;
  survivor?:LocationMergeMaster|null;
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
export interface V170CurrentTeamAssignment{teamId:number;teamCode:string;teamName:string;isPrimary:boolean}
export interface V170PeopleRow{userId:number;userCode:string;userType:string;employeeNo?:string;displayName:string;email?:string;employmentStatus?:string;adminEnabled:boolean;actualAccess:boolean;roles:string[];teamAssignments:V170CurrentTeamAssignment[];primaryTeamId?:number;primaryTeamName?:string;authorizationFrom?:string;authorizationTo?:string;hireDate?:string;terminationDate?:string}
export interface V170EmploymentPeriod{userEmploymentPeriodId:number;employmentStatus:string;effectiveFrom:string;effectiveTo?:string;sourceType:string;sourceReference?:string;isCurrent:boolean}
export interface V170PersonDetail{
  userId:number;userCode:string;userType:string;identityProvider:string;employeeNo?:string;displayName:string;email?:string;
  organizationId?:number;organizationName?:string;adminEnabled:boolean;actualAccess:boolean;employmentStatus?:string;
  externalOrganization?:string;externalTitle?:string;authorizationFrom?:string;authorizationTo?:string;
  employmentPeriods:V170EmploymentPeriod[];roleAssignments:unknown[];teamAssignments:unknown[];dataScopes:unknown[];capabilities:unknown[];
  employmentId?:number;hireDate?:string;terminationDate?:string;employmentRowVersion?:string;
}
export interface ManagedTeam{teamId:number;organizationId:number;teamCode:string;teamName:string;isActive:boolean}
export interface ManagedLocation{locationId:number;locationCode:string;teamId?:number;teamName?:string;locationName:string;locationType:string;city?:string;district?:string;address?:string;plusCode?:string;latitude?:number;longitude?:number;isTemporary:boolean;approvalStatus:string;geocodingStatus:string;isActive:boolean;createdAt:string;rowVersion:string;duplicateOfLocationId?:number|null;duplicateReason?:string|null}
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
