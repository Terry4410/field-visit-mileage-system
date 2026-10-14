export type MergeLocationPreview={locationId:number;locationName:string;address:string|null;city:string|null;district:string|null;plusCode:string|null;taxId:string|null;masterNote:string|null;rowVersion:string};
export type MergePreviewField={field:string;source:string;survivor:string;proposed:string};
const mergeFields=["LocationName","City","District","Address","PlusCode","TaxId","MasterNote"] as const;
export function mergePreviewFields(source:MergeLocationPreview,survivor:MergeLocationPreview,proposed:Partial<Record<typeof mergeFields[number],string|null>>):MergePreviewField[]{
 const values=(row:MergeLocationPreview,field:string)=>String((row as unknown as Record<string,unknown>)[field]??"");
 return mergeFields.map(field=>({field,source:values(source,field),survivor:values(survivor,field),proposed:String(proposed[field]??values(survivor,field))})).filter(x=>x.source!==x.survivor||x.proposed!==x.survivor);
}
export function mergeStatusLabel(status:string):string{switch(status){case"Pending":return"合併待審核";case"Rejected":return"合併已退回";case"Applied":return"合併已套用";default:return"合併狀態待查核";}}
export function mergeConflictMessage(code:string):string{if(code.includes("ROWVERSION"))return"來源或保留地點已被更新，請重新載入預覽後再提交。";if(code.includes("REFERENCE_CONFLICT"))return"目前引用存在衝突，未套用任何合併變更。";if(code.includes("INDETERMINATE"))return"結果不明，請查核 Request、Stage 與 Audit，禁止直接重送。";return"合併未完成，系統未發布變更。";}
