export type TeamLocationNoteState="NeverExisted"|"Active"|"Cleared";

export interface TeamLocationNote{
  state:TeamLocationNoteState;
  teamLocationNoteId?:number|null;
  teamId:number;
  locationId:number;
  note?:string|null;
  rowVersion?:string|null;
  updatedAt?:string|null;
}

export interface TeamLocationNoteTeam{
  teamId:number;
  organizationId:number;
  teamCode:string;
  teamName:string;
}

export const TEAM_LOCATION_NOTE_TEAMS_API="/team-location-notes/teams";

export function teamLocationNoteReadPath(teamId:number,locationId:number){
  return `/teams/${teamId}/location-notes?locationId=${locationId}`;
}

export function teamLocationNoteCreatePath(teamId:number){
  return `/teams/${teamId}/location-notes`;
}

export function teamLocationNoteUpdatePath(teamLocationNoteId:number){
  return `/team-location-notes/${teamLocationNoteId}`;
}

export function teamLocationNoteClearPath(teamLocationNoteId:number){
  return `/team-location-notes/${teamLocationNoteId}/clear`;
}

export function teamLocationNoteStateLabel(state:TeamLocationNoteState){
  if(state==="Active")return "使用中";
  if(state==="Cleared")return "已清除";
  return "尚未建立";
}
