const textEncoder=new TextEncoder();

const xmlEscape=(value:unknown)=>String(value??"")
  .replaceAll("&","&amp;")
  .replaceAll("<","&lt;")
  .replaceAll(">","&gt;")
  .replaceAll('"',"&quot;")
  .replaceAll("'","&apos;");

const columnName=(n:number)=>{
  let result="";
  for(let x=n;x>0;x=Math.floor((x-1)/26))result=String.fromCharCode(65+((x-1)%26))+result;
  return result;
};

const uint16=(value:number)=>{
  const out=new Uint8Array(2);
  new DataView(out.buffer).setUint16(0,value,true);
  return out;
};

const uint32=(value:number)=>{
  const out=new Uint8Array(4);
  new DataView(out.buffer).setUint32(0,value>>>0,true);
  return out;
};

const concatBytes=(parts:Uint8Array[])=>{
  const length=parts.reduce((sum,p)=>sum+p.length,0);
  const out=new Uint8Array(length);
  let offset=0;
  for(const part of parts){out.set(part,offset);offset+=part.length;}
  return out;
};

const crc32=(data:Uint8Array)=>{
  let crc=0xffffffff;
  for(const byte of data){
    crc^=byte;
    for(let i=0;i<8;i++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);
  }
  return (crc^0xffffffff)>>>0;
};

const zipStore=(entries:Array<{name:string;text:string}>)=>{
  const locals:Uint8Array[]=[];
  const centrals:Uint8Array[]=[];
  let offset=0;

  for(const entry of entries){
    const name=textEncoder.encode(entry.name);
    const data=textEncoder.encode(entry.text);
    const crc=crc32(data);
    const local=concatBytes([
      uint32(0x04034b50),uint16(20),uint16(0x0800),uint16(0),uint16(0),uint16(0),
      uint32(crc),uint32(data.length),uint32(data.length),uint16(name.length),uint16(0),
      name,data
    ]);
    locals.push(local);

    const central=concatBytes([
      uint32(0x02014b50),uint16(20),uint16(20),uint16(0x0800),uint16(0),uint16(0),uint16(0),
      uint32(crc),uint32(data.length),uint32(data.length),uint16(name.length),uint16(0),uint16(0),
      uint16(0),uint16(0),uint32(0),uint32(offset),name
    ]);
    centrals.push(central);
    offset+=local.length;
  }

  const centralStart=offset;
  const centralData=concatBytes(centrals);
  const end=concatBytes([
    uint32(0x06054b50),uint16(0),uint16(0),uint16(entries.length),uint16(entries.length),
    uint32(centralData.length),uint32(centralStart),uint16(0)
  ]);
  return concatBytes([...locals,centralData,end]);
};

const excelCell=(value:unknown,row:number,col:number)=>{
  const ref=`${columnName(col)}${row}`;
  if(typeof value==="number"&&Number.isFinite(value))return `<c r="${ref}"><v>${value}</v></c>`;
  if(typeof value==="boolean")return `<c r="${ref}" t="b"><v>${value?1:0}</v></c>`;
  return `<c r="${ref}" t="inlineStr"><is><t xml:space="preserve">${xmlEscape(value??"")}</t></is></c>`;
};

export function downloadExcel(filename:string,rows:Record<string,unknown>[],sheetName="查詢結果"){
  const safeSheet=(sheetName||"查詢結果").replace(/[\\/*?:[\]]/g," ").slice(0,31)||"查詢結果";
  const headers=rows.length?Object.keys(rows[0]):["查詢結果"];
  const dataRows=rows.length?rows:[{"查詢結果":"查無資料"}];
  const matrix:unknown[][]=[headers,...dataRows.map(r=>headers.map(h=>r[h]))];

  const sheetRows=matrix.map((row,rowIndex)=>
    `<row r="${rowIndex+1}">${row.map((value,colIndex)=>excelCell(value,rowIndex+1,colIndex+1)).join("")}</row>`
  ).join("");
  const lastCell=`${columnName(headers.length)}${matrix.length}`;

  const sheetXml=`<?xml version="1.0" encoding="UTF-8" standalone="yes"?>`+
    `<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">`+
    `<sheetData>${sheetRows}</sheetData><autoFilter ref="A1:${lastCell}"/></worksheet>`;

  const entries=[
    {name:"[Content_Types].xml",text:`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>`},
    {name:"_rels/.rels",text:`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>`},
    {name:"xl/workbook.xml",text:`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="${xmlEscape(safeSheet)}" sheetId="1" r:id="rId1"/></sheets></workbook>`},
    {name:"xl/_rels/workbook.xml.rels",text:`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>`},
    {name:"xl/worksheets/sheet1.xml",text:sheetXml}
  ];

  const bytes=zipStore(entries);
  const blob=new Blob([bytes],{type:"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"});
  const url=URL.createObjectURL(blob);
  const a=document.createElement("a");
  a.href=url;
  a.download=filename.toLowerCase().endsWith(".xlsx")?filename:filename.replace(/\.[^.]+$/,"")+".xlsx";
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(()=>URL.revokeObjectURL(url),0);
}

// 保留舊函式名稱，讓既有頁面不需全面重寫；實際下載已統一改為真正的 .xlsx。
export function downloadCsv(filename:string,rows:Record<string,unknown>[]){
  downloadExcel(filename.replace(/\.csv$/i,".xlsx"),rows);
}

// UI 統一由呼叫端自行加上貨幣符號，避免出現 $$2.50。
export const money=(n?:number)=>n==null?"—":n.toLocaleString("zh-TW",{minimumFractionDigits:2,maximumFractionDigits:2});
