// Optional Windows browser integration check. Node 22+ and installed Edge only;
// no npm packages or browser downloads. All fixtures stay in the approved temp root.
import { spawn, spawnSync } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';

const dll = process.argv[2];
if (!dll) throw new Error('Usage: node tests/web-ui-smoke.mjs PATH-TO-TRAINER-DLL');
const root = await mkdtemp(path.join(tmpdir(), 'opencode', 'dne-browser-'));
const workspace = path.join(root, 'project');
const alternate = path.join(root, 'alternate');
const profile = path.join(root, 'browser-profile');
await mkdir(workspace); await mkdir(alternate);
await writeFile(path.join(workspace, 'Example.cs'), '// original project\n');
await writeFile(path.join(alternate, 'Other.cs'), '// original alternate\n');
const listener = createServer(); await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve));
const port = listener.address().port; await new Promise(resolve => listener.close(resolve));
const address = `http://127.0.0.1:${port}`;
let server, browser, socket;
let serverLog = '', browserLog = '';
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(check, description, timeout = 20000) {
  const stop = Date.now() + timeout;
  while (Date.now() < stop) { try { if (await check()) return; } catch { } await delay(100); }
  throw new Error(`Timed out: ${description}`);
}
function stop(child) {
  if (!child?.pid || child.exitCode !== null) return;
  if (process.platform === 'win32') spawnSync('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
  else child.kill('SIGTERM');
}

try {
  server = spawn('dotnet', [path.resolve(dll), 'serve', '--port', String(port), '--models', path.join(root, 'models'), '--workspace', workspace], { windowsHide: true });
  server.stdout.on('data', chunk => serverLog += chunk); server.stderr.on('data', chunk => serverLog += chunk);
  await until(async () => (await fetch(address + '/api/ui/config')).ok, 'local UI server');
  browser = spawn(process.env.DNE_BROWSER || 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', [
    '--headless=new', '--disable-gpu', '--disable-background-networking', '--no-first-run', '--no-default-browser-check',
    '--remote-debugging-port=0', '--remote-allow-origins=*', '--window-size=1440,1000', `--user-data-dir=${profile}`, 'about:blank'
  ], { windowsHide: true });
  browser.stderr.on('data', chunk => browserLog += chunk);
  await until(() => /DevTools listening on (ws:\/\/[^\s]+)/.test(browserLog), 'headless Edge debugger');
  const browserSocket = /DevTools listening on (ws:\/\/[^\s]+)/.exec(browserLog)[1];
  const debugOrigin = browserSocket.replace(/^ws:/, 'http:').split('/devtools/')[0];
  const tabs = await (await fetch(debugOrigin + '/json/list')).json();
  socket = new WebSocket(tabs.find(tab => tab.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
  let id = 0;
  const waiting = new Map(); const exceptions = [];
  socket.addEventListener('message', event => {
    const value = JSON.parse(event.data);
    if (value.method === 'Runtime.exceptionThrown') exceptions.push(value.params.exceptionDetails.text);
    if (!value.id) return;
    const pending = waiting.get(value.id); waiting.delete(value.id);
    if (value.error) pending?.reject(new Error(JSON.stringify(value.error))); else pending?.resolve(value.result);
  });
  const command = (method, params = {}) => new Promise((resolve, reject) => { const number = ++id; waiting.set(number, { resolve, reject }); socket.send(JSON.stringify({ id: number, method, params })); });
  async function evaluate(expression) {
    const result = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
    return result.result.value;
  }
  await command('Page.enable'); await command('Runtime.enable'); await command('Page.navigate', { url: address });
  await until(() => evaluate("document.getElementById('workspaceStatus')?.textContent.includes('Selected')"), 'browser initialization and directory selection');
  await evaluate("document.getElementById('newModel').click(); document.querySelector('#modelForm input[name=name]').value='browser-model'; document.getElementById('modelForm').requestSubmit()");
  await until(() => evaluate("document.getElementById('modelSelect').value==='browser-model' && document.querySelectorAll('#files .file-item').length>0 && !document.getElementById('sendButton').disabled"), 'model creation and file browser');
  console.log('[PASS] Browser creates a model and initializes the scoped file browser.');
  await evaluate("document.getElementById('prompt').value='1+1'; document.getElementById('chatForm').requestSubmit()");
  await until(() => evaluate("[...document.querySelectorAll('.assistant .message-body')].some(element=>element.textContent.includes('= 2')) && !document.getElementById('sendButton').disabled"), 'a real engine chat reply');
  console.log('[PASS] Browser chat renders an actual engine response.');
  await evaluate(`document.getElementById('workspacePath').value=${JSON.stringify(alternate)}; document.getElementById('workspaceForm').requestSubmit()`);
  await until(() => evaluate(`document.getElementById('rootLabel').textContent===${JSON.stringify(alternate)} && [...document.querySelectorAll('#files .file-item')].some(button=>button.textContent==='Other.cs')`), 'development directory switch');
  await evaluate("[...document.querySelectorAll('#files .file-item')].find(button=>button.textContent==='Other.cs').click()");
  await until(() => evaluate("document.getElementById('editor').value.includes('original alternate') && !document.getElementById('editorView').hidden"), 'source editor');
  await evaluate("document.getElementById('editor').value='// edited through the browser\\n'; document.getElementById('editor').dispatchEvent(new Event('input',{bubbles:true})); document.getElementById('saveFile').click()");
  await until(async () => (await readFile(path.join(alternate, 'Other.cs'), 'utf8')).includes('edited through the browser'), 'hash-checked editor save');
  if (!(await readFile(path.join(workspace, 'Example.cs'), 'utf8')).includes('original project')) throw new Error('The wrong directory was modified.');
  console.log('[PASS] Switching directories and editing a source file modifies only the chosen development folder.');
  if (exceptions.length) throw new Error('Browser exceptions: ' + exceptions.join('; '));
  await evaluate("document.getElementById('chatTab').click(); document.getElementById('activityPanel').hidden=false");
  const screenshot = await command('Page.captureScreenshot', { format: 'png' });
  const screenshotPath = path.join(root, 'workspace.png');
  await writeFile(screenshotPath, Buffer.from(screenshot.data, 'base64'));
  console.log('[PASS] No uncaught frontend exceptions. Screenshot: ' + screenshotPath);
} catch (error) {
  console.error(error); console.error('Server log:', serverLog); console.error('Browser log:', browserLog); process.exitCode = 1;
} finally {
  socket?.close(); stop(browser); stop(server); await delay(300);
  await rm(profile, { recursive: true, force: true }).catch(() => {});
  await rm(workspace, { recursive: true, force: true }).catch(() => {});
  await rm(alternate, { recursive: true, force: true }).catch(() => {});
  await rm(path.join(root, 'models'), { recursive: true, force: true }).catch(() => {});
}
