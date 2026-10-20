export function filteredExportEndpoint(dataset:string,query:string){
 return "/admin/query-exports/"+dataset+".xlsx"+(query?"?"+query:"");
}
