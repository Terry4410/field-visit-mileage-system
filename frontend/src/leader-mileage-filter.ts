import type {Trip} from "./types";

export function isLeaderMileageCalculable(trip:Trip):boolean{
  const alreadyCalculated=trip.mileageSource==="GoogleMapsAPI"
    &&Boolean(trip.routeCalculationAttemptId)
    &&typeof trip.systemDistanceKm==="number"
    &&trip.systemDistanceKm>0;
  return trip.status!=="PendingApproval"&&!alreadyCalculated&&trip.stops.length>=1;
}

export function filterLeaderMileageRowsByDate<T extends Pick<Trip,"visitDate"|"status">>(
  rows:T[], mode:string, start:string, end:string
):T[]{
  if(mode!=="DateRange")return rows;
  return rows.filter(row=>row.status==="PendingApproval"||(
    (!start||row.visitDate>=start)&&(!end||row.visitDate<=end)
  ));
}
