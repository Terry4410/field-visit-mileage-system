export type ScopedLocation={teamId?:number|null;approvalStatus:string;isActive:boolean;createdByUserId?:number|null};
export const mayEditScopedDraft=(r:ScopedLocation,role:"visitor"|"leader",uid:number,teams:readonly number[])=>
 r.teamId!=null&&teams.includes(r.teamId)&&r.approvalStatus==="Pending"&&!r.isActive&&(role==="leader"||r.createdByUserId===uid);
