import type {ManagedLocation} from "./types";

export const mayEditVisitorDraft = (row: ManagedLocation, userId: number) =>
  row.createdByUserId === userId && row.approvalStatus === "Pending" && !row.isActive && row.locationType === "Customer";

// Frontend options only; server-side role, HR and team scope checks remain authoritative.
export const leaderScopedTeamOptions = (user: {teamScopes?: {teamId:number;teamName:string;isPrimary:boolean}[]} | null) =>
  user?.teamScopes?.filter(x => Number.isInteger(x.teamId) && x.teamId > 0) ?? [];
