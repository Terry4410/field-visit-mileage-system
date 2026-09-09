import type{V180Organization}from"./types";

export interface V180LeaderSummary{
  employmentId:number;
  displayName:string;
  delegateEmploymentId?:number|null;
  delegateDisplayName?:string|null;
}

export interface V180TeamAdmin{
  teamId:number;
  code:string;
  name:string;
  organization:V180Organization;
  effectiveFrom?:string|null;
  effectiveTo?:string|null;
  isActive:boolean;
  centerId?:number|null;
  centerCode?:string|null;
  centerName?:string|null;
  memberCount:number;
  leaders:V180LeaderSummary[];
  version:string;
}

export interface V180CenterAdmin{
  centerId:number;
  code:string;
  name:string;
  organization:V180Organization;
  effectiveFrom:string;
  effectiveTo?:string|null;
  isActive:boolean;
  version:string;
}

export interface V180TeamLifecycleDetail{
  teamId:number;
  organizationId:number;
  code:string;
  name:string;
  effectiveFrom?:string|null;
  effectiveTo?:string|null;
  isActive:boolean;
  notes?:string|null;
  version:string;
}

export interface V180CenterLifecycleDetail{
  centerId:number;
  organizationId:number;
  code:string;
  name:string;
  effectiveFrom:string;
  effectiveTo?:string|null;
  isActive:boolean;
  notes?:string|null;
  version:string;
}

export interface V180TeamCenterAssignment{
  teamCenterAssignmentId:number;
  teamId:number;
  centerId:number;
  centerCode:string;
  centerName:string;
  effectiveFrom:string;
  effectiveTo?:string|null;
  changeReason?:string|null;
  version:string;
}

export function v180LifecycleErrorMessage(error:unknown,fallback="儲存失敗"){
  const message=error instanceof Error?error.message:String(error||fallback);
  if(message.includes("ROWVERSION_CONFLICT"))return"資料已被其他人更新，請重新整理後再試。";
  if(message.includes("TEAM_DEACTIVATION_BLOCKED"))return"小組仍有生效或未來的成員、主管或中心關聯，請先結束相關設定再停用。";
  if(message.includes("CENTER_DEACTIVATION_BLOCKED"))return"中心仍有生效或未來的 Team-Center 關聯，請先結束相關設定再停用。";
  if(message.includes("LIFECYCLE_CONFLICT")||message.includes("OVERLAP")||message.includes("重疊"))return`生命週期衝突：${message}`;
  return message||fallback;
}

export function lifecycleStatus(from:string,to:string|null|undefined,asOf:string){
  if(from>asOf)return"未來";
  if(to&&to<asOf)return"已結束";
  return"生效中";
}

export function lifecycleEndDate(from:string|null|undefined,today:string){
  return from&&from>today?from:today;
}
