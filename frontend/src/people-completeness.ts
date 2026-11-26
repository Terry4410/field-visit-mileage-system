import type {V170PeopleRow} from "./types";

export function businessAccessLabel(u:V170PeopleRow):string{
 if(!u.roles.length)return "未指派業務角色（不可進入系統）";
 if(!u.actualAccess)return "人事／身分條件未符合，暫不可登入";
 return "人事及業務授權有效";
}

export function completenessWarnings(u:V170PeopleRow):string[]{
 const warnings:string[]=[];
 if(!u.employmentStatus)warnings.push("缺有效人事狀態");
 if(!u.roles.length)warnings.push("缺業務角色");
 if(u.roles.includes("visitor")&&!u.primaryDeploymentSiteId)
  warnings.push("外訪員缺主要派駐據點：可儲存人事資料，送出行程前需補齊");
 if(u.roles.some(r=>r==="visitor"||r==="leader")&&!u.teamAssignments.length)
  warnings.push("缺管理小組");
 return warnings;
}

// Current projections only; preserve history via existing effective-date APIs.
export function currentAssignments(detail:import("./types").V170PersonDetail){
 const roles=(detail.roleAssignments as {roleCode:string;isCurrent:boolean}[]).filter(x=>x.isCurrent).map(x=>x.roleCode);
 const teams=(detail.teamAssignments as {teamId:number;isPrimary:boolean;isCurrent:boolean}[]).filter(x=>x.isCurrent).map(x=>({teamId:x.teamId,isPrimary:x.isPrimary}));
 return {roles,teams};
}
export function validTeamSelection(teams:{teamId:number;isPrimary:boolean}[],required:boolean):boolean{
 return new Set(teams.map(x=>x.teamId)).size===teams.length&&(teams.length===0?!required:teams.filter(x=>x.isPrimary).length===1);
}
export function setupWarnings(roles:string[],teams:{teamId:number;isPrimary:boolean}[],hasPrimarySite:boolean|null):string[]{
 const result:string[]=[];
 if(!roles.length)result.push("業務角色未設定");
 if(!validTeamSelection(teams,roles.some(r=>r==="visitor"||r==="leader")))result.push("管理小組或主要小組未設定");
 if(roles.includes("visitor")&&hasPrimarySite!==true)result.push(hasPrimarySite===null?"主要派駐據點尚未確認":"主要派駐據點未設定");
 return result;
}
