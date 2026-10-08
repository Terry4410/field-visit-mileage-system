export type NavigationTitleItem={path:string;label:string};

export function resolveNavigationTitle(
 items:NavigationTitleItem[],
 pathname:string
){
 const exact=items.find(item=>item.path===pathname);
 if(exact)return exact.label;

 const prefix=items
  .filter(item=>item.path!=="/"&&pathname.startsWith(item.path+"/"))
  .sort((a,b)=>b.path.length-a.path.length)[0];

 return prefix?.label??items[0]?.label??"";
}
