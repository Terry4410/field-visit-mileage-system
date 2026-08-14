import type{CorrectionChange}from"./types";

const pendingStatuses=new Set([
  "PendingLeaderReview",
  "PendingAdminClose"
]);

const fieldLabels:Record<string,string>={
  VisitDate:"日期",
  StartTime:"開始時間",
  EndTime:"結束時間",
  Notes:"備註",
  ClaimedDistanceKm:"外訪員自算里程",
  SystemDistanceKm:"系統里程",
  ApprovedDistanceKm:"核定里程",
  RatePerKm:"每公里補助費率",
  RatePerKmSnapshot:"每公里補助費率",
  SubsidyAmount:"補助金額",
  Project:"專案",
  VisitType:"拜訪形式",
  VisitPurpose:"行程目的",
  LocationName:"地點名稱",
  Address:"地址"
};

export function isPendingCorrection(status?:string|null){
  return !!status&&pendingStatuses.has(status);
}

export function correctionFieldLabel(fieldName:string){
  if(fieldLabels[fieldName])return fieldLabels[fieldName];

  if(
    fieldName.startsWith("Stop")
    ||fieldName.startsWith("Stops")
  ){
    return "拜訪地點";
  }

  return fieldName;
}

export function correctionChangeText(change:CorrectionChange){
  const label=correctionFieldLabel(change.fieldName);

  const oldValue=
    change.oldValue===undefined
    ||change.oldValue===null
    ||change.oldValue===""
      ?"—"
      :change.oldValue;

  const newValue=
    change.newValue===undefined
    ||change.newValue===null
    ||change.newValue===""
      ?"—"
      :change.newValue;

  return `${label}：${oldValue} → ${newValue}`;
}
