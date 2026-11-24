export type AdminNavigationItem={path:string;label:string;short:string};

export const adminNavGroups:{label:string;items:AdminNavigationItem[]}[]=[
 {label:"營運管理",items:[
  {path:"/admin",label:"管理儀表板",short:"總覽"},
  {path:"/admin/query",label:"行程查詢",short:"查詢"},
  {path:"/admin/corrections",label:"更正管理",short:"更正"}
 ]},
 {label:"人事與組織",items:[
  {path:"/admin/users",label:"人員與權限",short:"人員"},
  {path:"/admin/teams",label:"小組與成員",short:"小組"}
 ]},
 {label:"業務主檔",items:[
  {path:"/admin/locations",label:"地點管理",short:"地點"},
  {path:"/admin/projects",label:"專案管理",short:"專案"},
  {path:"/admin/visit-types",label:"拜訪形式",short:"形式"},
  {path:"/admin/rates",label:"補助費率",short:"費率"}
 ]},
 {label:"報表與分析",items:[{path:"/admin/reports",label:"報表下載",short:"報表"}]}
];
export const adminNavItems=adminNavGroups.flatMap(group=>group.items);
