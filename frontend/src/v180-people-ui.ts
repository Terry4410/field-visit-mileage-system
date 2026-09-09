import type{V180PersonRow}from"./types";

export const V180_INTERNAL_ROLE_LABELS:Record<string,string>={
 visitor:"外訪員",
 leader:"小組長",
 admin:"管理者"
};

export function v180RoleCodes(person:V180PersonRow):string[]{
 return person.roles.map(role=>role.code.trim().toLowerCase()==="government"?"supervisor":role.code.trim().toLowerCase());
}

export function isV180Supervisor(person:V180PersonRow):boolean{
 return v180RoleCodes(person).includes("supervisor");
}

export function canEditV180InternalAccess(person:V180PersonRow):boolean{
 return person.adminEnabled!==null&&person.adminEnabled!==undefined&&!isV180Supervisor(person);
}

export function v180AccessErrorMessage(error:unknown,fallback="儲存失敗"):string{
 const message=error instanceof Error?error.message:fallback;
 return message.includes("ROWVERSION_CONFLICT")
  ?"資料已被其他管理者修改，請重新整理後再試。"
  :message;
}
