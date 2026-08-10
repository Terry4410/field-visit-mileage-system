export function downloadCsv(filename:string, rows:Record<string,unknown>[]){
 const headers=rows.length?Object.keys(rows[0]):[]; const esc=(v:unknown)=>`"${String(v??"").replaceAll('"','""')}"`;
 const text="\ufeff"+[headers.map(esc).join(","),...rows.map(r=>headers.map(h=>esc(r[h])).join(","))].join("\r\n");
 const url=URL.createObjectURL(new Blob([text],{type:"text/csv;charset=utf-8"})); const a=document.createElement("a");a.href=url;a.download=filename;a.click();URL.revokeObjectURL(url);
}
export const money=(n?:number)=>n==null?"—":`$${n.toLocaleString("zh-TW",{minimumFractionDigits:2,maximumFractionDigits:2})}`;
