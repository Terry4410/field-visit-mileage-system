import type{ImportPreview}from"./types";

export type ExcelCanonicalCreateResult={
 importBatchId:string;submitted:number;alreadySubmitted:number;
 status:string;requestPublicIds:string[];
};

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
