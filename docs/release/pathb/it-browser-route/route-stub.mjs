import http from 'node:http';
// Local-only simulated response; no external network or real API key.
http.createServer((req,res)=>{
 if(req.method!=='POST'||req.url!=='/directions/v2:computeRoutes'){res.writeHead(404);res.end();return}
 let body='';req.on('data',c=>body+=c);req.on('end',()=>{
 try{const p=JSON.parse(body);if(!p.origin?.address||!p.destination?.address||!p.intermediates?.length)throw Error('route inputs');
 console.log('LOCAL_ROUTE_STUB_REQUEST=PASS');
 res.writeHead(200,{'Content-Type':'application/json'});res.end(JSON.stringify({routes:[{distanceMeters:12300,duration:'1800s',polyline:{encodedPolyline:'test'}}]}));
 }catch{res.writeHead(400);res.end('{}')}
 });
}).listen(18378,'127.0.0.1');
