import type {ManagedLocation} from "./types";

export const mayEditVisitorDraft = (row: ManagedLocation, userId: number) =>
  row.createdByUserId === userId && row.approvalStatus === "Pending" && !row.isActive && row.locationType === "Customer";
