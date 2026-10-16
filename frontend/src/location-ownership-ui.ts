import type {ManagedLocation} from "./types";

const isEditableDraft = (row: ManagedLocation) =>
  row.approvalStatus === "Pending" && !row.isActive && row.locationType === "Customer";

export const mayEditVisitorDraft = (row: ManagedLocation, userId: number) =>
  row.createdByUserId === userId && isEditableDraft(row);

export const mayEditLeaderManagedDraft = (
  row: ManagedLocation,
  teamIds: readonly number[] | undefined
) => !!row.teamId && !!teamIds?.includes(row.teamId) && isEditableDraft(row);

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
