import http from 'node:http';
let calls = 0;
const server = http.createServer((req, res) => {
  if (req.url !== '/v1/models' || req.method !== 'GET' || req.headers.authorization !== 'Bearer settings-test-key') {
    res.writeHead(400).end(); return;
  }
  calls++;
  if (calls === 2) { res.writeHead(401).end('{"error":"sensitive-provider-body"}'); return; }
  if (calls === 3) { const timer = setTimeout(() => res.writeHead(503).end(), 20000); res.on('close', () => clearTimeout(timer)); return; }
  res.writeHead(200, { 'Content-Type': 'application/json', Server: 'llama.cpp' }).end(JSON.stringify({ data: [
    { id: 'deepseek-flash' }, { id: 'deepseek-reasoner' }, { id: 'deepseek-flash' }, { id: 123 },
    { id: 'provider/models/long-model-id-for-testing-horizontal-text-layout-2026-09' }
  ] }));
});
server.listen(0, '127.0.0.1', () => console.log(JSON.stringify({ port: server.address().port })));
process.on('SIGTERM', () => { server.closeAllConnections(); server.close(); });
