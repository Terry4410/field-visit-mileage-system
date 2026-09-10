import{describe,expect,it}from"vitest";
import{
 TEAM_LOCATION_NOTE_TEAMS_API,
 teamLocationNoteClearPath,
 teamLocationNoteCreatePath,
 teamLocationNoteReadPath,
 teamLocationNoteStateLabel,
 teamLocationNoteUpdatePath
}from"./team-location-note";

describe("team location note runtime UI contract",()=>{
 it("uses the frozen API routes",()=>{
  expect(TEAM_LOCATION_NOTE_TEAMS_API).toBe("/team-location-notes/teams");
  expect(teamLocationNoteReadPath(12,34)).toBe("/teams/12/location-notes?locationId=34");
  expect(teamLocationNoteCreatePath(12)).toBe("/teams/12/location-notes");
  expect(teamLocationNoteUpdatePath(56)).toBe("/team-location-notes/56");
  expect(teamLocationNoteClearPath(56)).toBe("/team-location-notes/56/clear");
 });
 it("distinguishes NeverExisted Active and Cleared",()=>{
  expect(teamLocationNoteStateLabel("NeverExisted")).toBe("尚未建立");
  expect(teamLocationNoteStateLabel("Active")).toBe("使用中");
  expect(teamLocationNoteStateLabel("Cleared")).toBe("已清除");
 });
});
