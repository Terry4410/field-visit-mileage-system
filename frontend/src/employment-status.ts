export function employmentStatusLabel(status?:string|null){
 switch((status||" ").trim().toLowerCase()){
  case "active":return "在職";
  case "leave":return "留停";
  case "terminated":return "離職";
  case "prehire":return "未到職";
  default:return status||"資料不完整";
 }
}
