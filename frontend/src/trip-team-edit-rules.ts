import type{TeamScope}from"./types";

export interface TripTeamEditResolution{
  teamId?:number;
  teamName?:string;
  originalTeamStillAllowed:boolean;
  reassigned:boolean;
  warning?:string;
}

export function resolveTripTeamForEdit(
  tripTeamId:number|undefined,
  tripTeamName:string|undefined,
  teamScopes:TeamScope[],
  primaryTeamId?:number,
  primaryTeamName?:string
):TripTeamEditResolution{
  const original=
    tripTeamId
      ?teamScopes.find(x=>x.teamId===tripTeamId)
      :undefined;

  if(original){
    return{
      teamId:original.teamId,
      teamName:original.teamName,
      originalTeamStillAllowed:true,
      reassigned:false
    };
  }

  const fallback=
    teamScopes.find(x=>x.isPrimary)
    ||(primaryTeamId
      ?teamScopes.find(x=>x.teamId===primaryTeamId)
      :undefined)
    ||teamScopes[0];

  const oldName=
    tripTeamName
    ||(tripTeamId?`Team ${tripTeamId}`:"原小組");

  if(!fallback){
    return{
      teamId:undefined,
      teamName:undefined,
      originalTeamStillAllowed:false,
      reassigned:false,
      warning:
        `原行程歸屬小組「${oldName}」已不在目前有效授權範圍，且目前沒有可使用的小組。請聯絡管理者後再處理此草稿。`
    };
  }

  const fallbackName=
    fallback.teamName
    ||primaryTeamName
    ||`Team ${fallback.teamId}`;

  return{
    teamId:fallback.teamId,
    teamName:fallbackName,
    originalTeamStillAllowed:false,
    reassigned:true,
    warning:
      `原行程歸屬小組「${oldName}」已不在目前有效授權範圍，已改為主要小組「${fallbackName}」。原拜訪地點與自行里程已清除，請重新選擇地點與專案後再儲存送出。`
  };
}
