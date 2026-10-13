export type OptionCRequest={requestPublicId:string;entityId:string;teamId:number|null;riskCode:string;status:string;requestedByUserId:number;submittedAt:string;beforeJson:string|null;proposedJson:string;rowVersion:string};
export const fields=["LocationName","City","District","Address","PlusCode","TaxId","MasterNote"] as const;
type Field=typeof fields[number];
export function safeFields(json:string|null):Record<Field,string>{
 const result=Object.fromEntries(fields.map(f=>[f,""])) as Record<Field,string>;
 if(!json||json.length>10000)return result;
 try{const data:unknown=JSON.parse(json);if(!data||typeof data!=="object"||Array.isArray(data))return result;
  for(const key of fields){const v=(data as Record<string,unknown>)[key];if(typeof v==="string")result[key]=v;}
 }catch{/* fail closed */}return result;
}
export function differences(row:OptionCRequest){
 const before=safeFields(row.beforeJson),after=safeFields(row.proposedJson);
 return fields.filter(k=>before[k]!==after[k]).map(k=>({field:k,before:before[k],after:after[k]}));
}
export function mayReview(row:OptionCRequest,actorId:number){
 return actorId>0&&actorId!==row.requestedByUserId&&row.status==="Pending"&&row.riskCode==="High"&&!!row.rowVersion;
}
export function reviewPayload(row:OptionCRequest,reason:string,decisionKey:string){
 const trimmed=reason.trim();
 if(!trimmed||trimmed.length>1000||!decisionKey||!row.rowVersion)throw new Error("請提供審核理由與有效決策識別碼。");
 return {requestRowVersion:row.rowVersion,decisionKey,reason:trimmed};
}
