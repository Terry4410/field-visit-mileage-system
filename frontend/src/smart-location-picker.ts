export interface LocationSearchPathInput{
  query?:string;
  city?:string;
  district?:string;
  projectId?:number;
  page?:number;
  pageSize?:number;
}

export function hasLocationSearchCriteria(
  input:Pick<LocationSearchPathInput,"query"|"city"|"district">
){
  return Boolean(
    input.query?.trim()
    ||input.city?.trim()
    ||input.district?.trim()
  );
}

export function buildLocationSearchPath(
  input:LocationSearchPathInput
){
  const params=new URLSearchParams();

  const query=input.query?.trim();
  const city=input.city?.trim();
  const district=input.district?.trim();

  if(query)params.set("q",query);
  if(city)params.set("city",city);
  if(district)params.set("district",district);

  if(input.projectId&&input.projectId>0)
    params.set("projectId",String(input.projectId));

  params.set("page",String(input.page&&input.page>0?input.page:1));
  params.set(
    "pageSize",
    String(input.pageSize&&input.pageSize>0?input.pageSize:20)
  );

  return `/locations/search?${params.toString()}`;
}

export function moveFavoriteIds(
  ids:number[],
  locationId:number,
  delta:-1|1
){
  const index=ids.indexOf(locationId);

  if(index<0)return ids;

  const nextIndex=index+delta;

  if(nextIndex<0||nextIndex>=ids.length)
    return ids;

  const next=[...ids];

  [next[index],next[nextIndex]]=[
    next[nextIndex],
    next[index]
  ];

  return next;
}
