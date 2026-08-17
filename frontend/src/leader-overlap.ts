import type{Trip}from"./types";

function toMinutes(value?:string){
  if(!value)return null;
  const parts=value.split(":");
  if(parts.length<2)return null;
  const h=Number(parts[0]);
  const m=Number(parts[1]);
  if(!Number.isFinite(h)||!Number.isFinite(m))return null;
  return h*60+m;
}

export function formatTripTime(trip:Pick<Trip,"startTime"|"endTime">){
  if(!trip.startTime||!trip.endTime)return "—";
  return `${trip.startTime.slice(0,5)}～${trip.endTime.slice(0,5)}`;
}

export function hasQueueTimeOverlap(trip:Trip,rows:Trip[]){
  const start=toMinutes(trip.startTime);
  const end=toMinutes(trip.endTime);
  if(start===null||end===null)return false;
  return rows.some(other=>{
    if(other.visitTripId===trip.visitTripId)return false;
    if(other.userId!==trip.userId)return false;
    if(other.visitDate!==trip.visitDate)return false;
    const otherStart=toMinutes(other.startTime);
    const otherEnd=toMinutes(other.endTime);
    if(otherStart===null||otherEnd===null)return false;
    return start<otherEnd&&end>otherStart;
  });
}

export function hasLeaderTimeOverlap(trip:Trip,rows:Trip[]){
  return Boolean(trip.hasTimeOverlapWarning)||hasQueueTimeOverlap(trip,rows);
}

export function leaderOverlapWarningText(trip:Trip,rows:Trip[]){
  if(!hasLeaderTimeOverlap(trip,rows))return null;
  return trip.timeOverlapConfirmed
    ?"⚠ 時間重疊｜外訪員已確認"
    :"⚠ 時間重疊";
}

export function leaderOverlapConfirmMessage(trip:Trip,rows:Trip[]){
  if(!hasLeaderTimeOverlap(trip,rows))return null;
  const visitorConfirmation=trip.timeOverlapConfirmed
    ?"\n外訪員已確認時間正確並仍送出。"
    :"";
  return `此行程與同日其他行程時間重疊。${visitorConfirmation}\n\n是否仍要核准？`;
}
