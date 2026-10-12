export type ScopedLocation={teamId?:number|null;approvalStatus:string;isActive:boolean;createdByUserId?:number|null};
export const mayEditScopedDraft=(r:ScopedLocation,role:"visitor"|"leader",uid:number,teams:readonly number[])=>
 // UI must not suggest that a Leader can edit a peer's draft merely because
 // the JWT has a team scope.  The server deliberately requires independent
 // VAL-B1-002 manager-grant provenance, which is currently unavailable.
 // A Leader who also holds Visitor may edit the owned draft only after
 // switching to the Visitor workflow; the server remains authoritative.
 r.teamId!=null&&teams.includes(r.teamId)&&r.approvalStatus==="Pending"&&!r.isActive&&r.createdByUserId===uid;
