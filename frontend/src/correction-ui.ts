import type{
  CorrectionChange
}from"./types";

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
  VisitPurpose:"拜訪目的",
  LocationName:"地點名稱",
  Address:"地址",
  Stops:"拜訪地點"
};

type StopValue={
  stopSequence?:number;
  locationCode?:string|null;
  locationName?:string|null;
  address?:string|null;
  projectCode?:string|null;
  projectName?:string|null;
  visitTypeCode?:string|null;
  visitTypeName?:string|null;
  visitPurpose?:string|null;
  notes?:string|null;
};

const stopFieldLabels:Record<keyof StopValue,string>={
  stopSequence:"順序",
  locationCode:"地點代碼",
  locationName:"地點名稱",
  address:"地址",
  projectCode:"專案代碼",
  projectName:"專案",
  visitTypeCode:"拜訪形式代碼",
  visitTypeName:"拜訪形式",
  visitPurpose:"拜訪目的",
  notes:"備註"
};

const stopComparableFields:(keyof StopValue)[]=[
  "locationName",
  "address",
  "projectName",
  "visitTypeName",
  "visitPurpose",
  "notes"
];

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

function displayValue(value:unknown){
  if(
    value===undefined
    ||value===null
    ||String(value).trim()===""
  ){
    return "—";
  }

  return String(value);
}

function parseStops(value?:string|null):StopValue[]|null{
  if(!value)return [];

  try{
    const parsed:unknown=JSON.parse(value);

    return Array.isArray(parsed)
      ?parsed as StopValue[]
      :null;
  }catch{
    return null;
  }
}

function stopName(
  stop:StopValue|undefined,
  sequence:number
){
  const name=stop?.locationName?.trim();

  return name
    ?`第 ${sequence} 站（${name}）`
    :`第 ${sequence} 站`;
}

function formatStopsChange(change:CorrectionChange){
  const oldStops=parseStops(change.oldValue);
  const newStops=parseStops(change.newValue);

  if(!oldStops||!newStops){
    return "拜訪地點：內容已變更";
  }

  const oldBySequence=new Map<number,StopValue>();
  const newBySequence=new Map<number,StopValue>();

  oldStops.forEach((stop,index)=>
    oldBySequence.set(
      stop.stopSequence??index+1,
      stop
    )
  );

  newStops.forEach((stop,index)=>
    newBySequence.set(
      stop.stopSequence??index+1,
      stop
    )
  );

  const sequences=Array.from(
    new Set([
      ...oldBySequence.keys(),
      ...newBySequence.keys()
    ])
  ).sort((a,b)=>a-b);

  const parts:string[]=[];

  for(const sequence of sequences){
    const oldStop=oldBySequence.get(sequence);
    const newStop=newBySequence.get(sequence);

    if(!oldStop&&newStop){
      parts.push(
        `新增${stopName(newStop,sequence)}`
      );
      continue;
    }

    if(oldStop&&!newStop){
      parts.push(
        `刪除${stopName(oldStop,sequence)}`
      );
      continue;
    }

    if(!oldStop||!newStop)continue;

    for(const field of stopComparableFields){
      const oldValue=oldStop[field];
      const newValue=newStop[field];

      if(oldValue===newValue)continue;

      parts.push(
        `${stopName(newStop,sequence)}－${stopFieldLabels[field]}：${displayValue(oldValue)} → ${displayValue(newValue)}`
      );
    }
  }

  return parts.length
    ?parts.join("；")
    :"拜訪地點：內容已變更";
}

export function correctionChangeText(change:CorrectionChange){
  if(change.fieldName==="Stops"){
    return formatStopsChange(change);
  }

  const label=correctionFieldLabel(change.fieldName);

  return `${label}：${displayValue(change.oldValue)} → ${displayValue(change.newValue)}`;
}
