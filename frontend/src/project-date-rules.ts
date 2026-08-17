import type {Project} from "./types";

export function isProjectAvailableOn(
  project:Project,
  visitDate:string
){
  if(!project.isActive)return false;
  if(!visitDate)return false;

  if(project.startDate&&visitDate<project.startDate)
    return false;

  if(project.endDate&&visitDate>project.endDate)
    return false;

  return true;
}
