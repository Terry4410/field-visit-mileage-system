import type {ManagedLocation} from "./types";

const isEditableDraft = (row: ManagedLocation) =>
  row.approvalStatus === "Pending" && !row.isActive && row.locationType === "Customer"
  && !row.isTemporary && !row.duplicateOfLocationId;

export const mayEditVisitorDraft = (row: ManagedLocation, userId: number) =>
  row.createdByUserId === userId && isEditableDraft(row);

export const mayEditLeaderManagedDraft = (
  row: ManagedLocation,
  teamIds: readonly number[] | undefined
) => !!row.teamId && !!teamIds?.includes(row.teamId) && isEditableDraft(row);

export const mayMaintainLeaderPublishedLowRisk = (
  row: ManagedLocation,
  teamIds: readonly number[] | undefined
) => !!row.teamId && !!teamIds?.includes(row.teamId)
  && row.locationType === "Customer" && row.approvalStatus === "Approved"
  && row.isActive && !row.isTemporary;

export const mayEditScopedDraft = (
  row: ManagedLocation,
  user: {userId:number;teamScopes?:{teamId:number;teamName:string;isPrimary:boolean}[]} | null,
  role: "visitor" | "leader"
) => role === "leader"
  ? mayEditLeaderManagedDraft(row, user?.teamScopes?.map(x => x.teamId))
  : !!user && mayEditVisitorDraft(row, user.userId);

// Frontend options only; server-side role, HR and team scope checks remain authoritative.
export const leaderScopedTeamOptions = (user: {teamScopes?: {teamId:number;teamName:string;isPrimary:boolean}[]} | null) =>
  user?.teamScopes?.filter(x => Number.isInteger(x.teamId) && x.teamId > 0) ?? [];

/** Note team options must come from explicit authenticated scopes or the
 * effective Visitor assignment endpoint. A selected trip team only narrows. */
export const scopedLocationNoteTeams = (
  teams: readonly {teamId:number;teamName:string;isPrimary:boolean}[] | undefined,
  teamId?: number | null
) => (teams ?? []).filter(t => Number.isInteger(t.teamId) && t.teamId > 0 &&
  (teamId == null || t.teamId === teamId));
