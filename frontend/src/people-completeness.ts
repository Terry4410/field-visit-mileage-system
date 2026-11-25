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
