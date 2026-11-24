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

/** Published master changes are separate from team notes; the backend must
 * independently recheck effective membership, ownership and row version. */
export const mayRequestPublishedChange = (
  row: ManagedLocation,
  userId: number | undefined,
  role: "visitor" | "leader",
  teamIds: readonly number[]
) => !!userId && !!row.teamId && teamIds.includes(row.teamId)
  && row.locationType === "Customer" && row.approvalStatus === "Approved"
  && row.isActive && !row.isTemporary && !row.duplicateOfLocationId
  && !row.duplicateReason?.trim() && !!row.rowVersion
  && (role === "leader" || row.createdByUserId === userId);

export const validTaiwanTaxId = (input: string) => {
  const value=input.trim();
  if(!/^[0-9]{8}$/.test(value)||value==="00000000")return false;
  const weights=[1,2,1,2,1,2,4,1];
  const sum=weights.reduce((acc,w,i)=>{const p=Number(value[i])*w;return acc+Math.floor(p/10)+p%10;},0);
  return sum%5===0||(value[6]==="7"&&(sum+1)%5===0);
};

export const firstTaxIdPayload = (row: ManagedLocation, input: string, operationKey: string) => {
  if(row.taxId?.trim()||!row.rowVersion||!operationKey||!validTaiwanTaxId(input))
    throw new Error("僅可首次補登有效的八位數統一編號。");
  return {expectedRowVersion:row.rowVersion,masterNote:null,firstTaxId:input.trim(),
    reason:"首次補登統一編號",operationKey};
};
