const fs=require('node:fs');const {spawn}=require('node:child_process');
(async()=>{
const data=JSON.parse(fs.readFileSync('/tmp/leader-rc-browser.json','utf8'));
if(!data.database.startsWith('LeaderRcIsolated_')||!data.connectionString.includes('localhost,1433'))throw Error('NONLOCAL_FIXTURE_DENIED');
const env={...process.env,ASPNETCORE_URLS:'http://127.0.0.1:5080',ConnectionStrings__DefaultConnection:data.connectionString,Auth__JwtKey:'leader-rc-isolated-key-at-least-32-characters',Auth__Mode:'Demo',BackgroundJobs__WorkerEnabled:'false',BackgroundJobs__RecoverOnStartup:'false',Cors__AllowedOrigins__0:'http://127.0.0.1:4173'};
const log=fs.openSync('/tmp/leader-rc-api.log','w');
const child=spawn('dotnet',['backend/src/FieldVisit.Api/bin/Release/net8.0/FieldVisit.Api.dll'],{env,detached:true,stdio:['ignore',log,log]});child.unref();
fs.writeFileSync('frontend/dist/config.js','window.APP_CONFIG={API_BASE_URL:"http://127.0.0.1:5080/api/v1",AUTH_MODE:"Demo"};');
for(let i=0;i<60;i++){try{const r=await fetch('http://127.0.0.1:5080/health/live');if(r.ok){console.log('ISOLATED_LOCAL_API_READY');return}}catch{}await new Promise(r=>setTimeout(r,500))}throw Error('ISOLATED_API_NOT_READY');
})().catch(e=>{console.error(e.message);process.exit(1)});
