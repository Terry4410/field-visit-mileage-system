export interface ManagedLocationGovernanceDetail{
  locationId:number;
  locationCode:string;
  locationName:string;
  address?:string|null;
  taxId?:string|null;
  masterNote?:string|null;
  duplicateOfLocationId?:number|null;
  duplicateReason?:string|null;
  rowVersion:string;
}
export interface ManagedLocationGovernanceSummary{locationId:number;taxId?:string|null;hasDuplicateReference:boolean}
export interface ManagedLocationGovernanceCandidate{locationId:number;locationCode:string;locationName:string;address?:string|null;taxId?:string|null;isActive:boolean}
export interface ManagedLocationGovernanceCandidatePage{items:ManagedLocationGovernanceCandidate[];page:number;pageSize:number;totalCount:number;totalPages:number}
export interface ManagedLocationInactivationBlocker{deploymentSiteId:number;deploymentSiteCode:string;deploymentSiteName:string;effectiveFrom:string;effectiveTo?:string|null}
export interface ManagedLocationInactivationImpact{locationId:number;canDeactivate:boolean;blockers:ManagedLocationInactivationBlocker[]}
