import type {ReactNode} from "react";
export default function DetailLink({children,onClick,ariaLabel}:{children:ReactNode;onClick:()=>void;ariaLabel?:string}){
 return <button type="button" aria-label={ariaLabel} onClick={onClick} style={{background:"none",border:0,padding:"4px 2px",color:"#175483",textDecoration:"underline",font:"inherit",fontWeight:600,textAlign:"left",cursor:"pointer",overflowWrap:"anywhere"}}>{children}</button>;
}
