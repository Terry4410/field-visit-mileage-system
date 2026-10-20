import type{ImportPreview}from"./types";

export type ExcelCanonicalCreateResult={
 importBatchId:string;submitted:number;alreadySubmitted:number;
 status:string;requestPublicIds:string[];
};

export const mayShowLegacyExcelConfirm=(reviewOnly:boolean)=>!reviewOnly;

// Mixed updates, non-canonical errors and truncated preview arrays cannot
// enter the explicit canonical Create flow. Backend independently revalidates
// live HR, batch ownership, team and every staged row before SQL writes.
export function maySubmitCanonicalExcelCreates(preview:ImportPreview):boolean{
 if(preview.importType!=="locations"||preview.totalCount===0
  ||preview.items.length!==preview.totalCount)return false;
 const create=preview.items.filter(x=>x.action==="Create");
 return create.length>0&&preview.items.every(x=>
  x.entityType==="Location"&&(
   (x.action==="Create"&&x.status==="Error"
    &&x.errorMessage==="LOCATION_EXCEL_CREATE_REQUIRES_CANONICAL_REVIEW")
   ||(x.action==="NoChange"&&x.status==="Valid")));
}

/** Excel published Update is review-only; mixed Create/Update fails closed. */
export function maySubmitCanonicalExcelUpdates(preview:ImportPreview):boolean{
 if(preview.importType!=="locations"||preview.totalCount===0
  ||preview.items.length!==preview.totalCount)return false;
 const update=preview.items.filter(x=>x.action==="Update");
 return update.length>0&&preview.items.every(x=>
  x.entityType==="Location"&&(
   (x.action==="Update"&&x.status==="Error"
    &&x.errorMessage==="LOCATION_EXCEL_PUBLISHED_CHANGE_REQUIRES_UNIFIED_APPROVAL")
   ||(x.action==="NoChange"&&x.status==="Valid")));
}

export type ExcelCanonicalRowLink={
 rowNumber:number;importBatchItemId:number;requestPublicId:string;
 operationCode:string;riskCode:string;requestStatus:string;
};
