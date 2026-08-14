import type{
  PeopleBulkConfirmResult,
  PeopleBulkPreview
}from"./types";

export function canConfirmPeopleBulk(
  preview:PeopleBulkPreview
){
  return preview.errorCount===0
    && preview.validCount>0;
}

export function peopleBulkActionLabel(
  action:string
){
  switch(action){
    case"Create":
      return"新增";
    case"Update":
      return"更新";
    case"NoChange":
      return"無異動";
    default:
      return action||"—";
  }
}

export function peopleBulkStatusLabel(
  status:string
){
  switch(status){
    case"Valid":
      return"正確";
    case"Error":
      return"錯誤";
    case"Applied":
      return"已套用";
    case"Failed":
      return"失敗";
    default:
      return status||"—";
  }
}

export function peopleBulkResultMessage(
  result:PeopleBulkConfirmResult
){
  return [
    `新增 ${result.created}`,
    `更新 ${result.updated}`,
    `無異動 ${result.unchanged}`,
    `失敗 ${result.failed}`
  ].join("｜");
}

export function peopleBulkHasPartialFailure(
  result:PeopleBulkConfirmResult
){
  return result.failed>0;
}
