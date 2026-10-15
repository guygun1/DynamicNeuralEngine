'use strict';
const $ = id => document.getElementById(id);
const state = { model: '', workspaceId: '', root: '', files: [], skills: [], messages: [], busy: false, abort: null,
  editor: { path: '', hash: '', dirty: false, truncated: false }, config: {} };
let toastTimer;

async function api(path, body, signal) {
  const response = await fetch(path, { method: body === undefined ? 'GET' : 'POST',
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body), signal });
  const text = await response.text();
  let result;
  try { result = JSON.parse(text); } catch { throw new Error(`Unexpected server response (${response.status}).`); }
  if (!response.ok) throw new Error(result.error || `Request failed (${response.status}).`);
  return result;
}
function toast(text, error = false) {
  clearTimeout(toastTimer); $('toast').textContent = text; $('toast').className = 'toast' + (error ? ' error' : '');
  $('toast').hidden = false; toastTimer = setTimeout(() => $('toast').hidden = true, 6500);
}
function scope() {
  if (!state.model) throw new Error('Choose or create a model first.');
  return { model: state.model, ...(state.workspaceId ? { workspace_id: state.workspaceId } : {}) };
}
function setBusy(value) {
  state.busy = value; state.abort = value ? new AbortController() : null;
  $('sendButton').disabled = value; $('stopButton').hidden = !value;
  $('modelSelect').disabled = value; $('pickFolder').disabled = value || !state.config.native_picker;
  $('workspaceForm').querySelector('button').disabled = value;
  $('newChat').disabled = value; $('newModel').disabled = value;
}
function showTab(tab) {
  $('chatView').hidden = tab !== 'chat'; $('editorView').hidden = tab !== 'editor';
  for (const name of ['chat', 'editor']) { $(name + 'Tab').classList.toggle('active', tab === name); $(name + 'Tab').setAttribute('aria-selected', String(tab === name)); }
}
function activity(name, args, result, milliseconds) {
  const item = document.createElement('details'); item.className = result.ok === false ? 'failed' : '';
  const summary = document.createElement('summary'); summary.textContent = `${result.ok === false ? '×' : '✓'} ${name}`;
  const duration = document.createElement('span'); duration.className = 'duration'; duration.textContent = `${Math.round(milliseconds)} ms`;
  const pre = document.createElement('pre'); const json = JSON.stringify({ arguments: args, result }, null, 2);
  pre.textContent = json.length > 24000 ? json.slice(0, 24000) + '\n[display truncated]' : json;
  item.append(summary, duration, pre); $('activity').prepend(item);
  while ($('activity').children.length > 30) $('activity').lastElementChild.remove();
}
async function callTool(name, args = {}, signal = state.abort?.signal) {
  const skill = state.skills.find(item => item.name === name);
  if ((skill?.requires_workspace || name.startsWith('workspace.') || name === 'code.synthesize') && !state.workspaceId)
    throw new Error('Choose a development directory first.');
  const start = performance.now();
  try {
    const result = await api('/api/skills/execute', { ...scope(), skill: name, arguments: args }, signal);
    activity(name, args, result, performance.now() - start); return result;
  } catch (error) { activity(name, args, { ok: false, error: error.message }, performance.now() - start); throw error; }
}
function ensureSuccess(result) { if (!result.ok) throw new Error(result.error || 'The operation did not succeed.'); return result.data; }
function richText(parent, text) {
  const links = /https?:\/\/[^\s<>\]]+/g; let previous = 0;
  for (const match of text.matchAll(links)) {
    parent.append(document.createTextNode(text.slice(previous, match.index)));
    const link = document.createElement('a'); link.href = match[0]; link.textContent = match[0]; link.target = '_blank'; link.rel = 'noopener noreferrer';
    parent.append(link); previous = match.index + match[0].length;
  }
  parent.append(document.createTextNode(text.slice(previous)));
}
function formatMessage(parent, text) {
  const blocks = /```[^\n]*\n([\s\S]*?)```/g; let previous = 0;
  for (const block of text.matchAll(blocks)) {
    richText(parent, text.slice(previous, block.index));
    const wrapper = document.createElement('div'); wrapper.className = 'code-block';
    const pre = document.createElement('pre'); const code = document.createElement('code'); code.textContent = block[1]; pre.append(code);
    const copy = document.createElement('button'); copy.className = 'copy-code'; copy.textContent = 'Copy';
    copy.addEventListener('click', () => navigator.clipboard.writeText(block[1]).then(() => toast('Code copied.')).catch(error => toast(error.message, true)));
    wrapper.append(pre, copy); parent.append(wrapper); previous = block.index + block[0].length;
  }
  richText(parent, text.slice(previous));
}
function appendMessage(role, content, options = {}) {
  const message = { role, content, tool: !!options.tool, note: options.note || '', error: !!options.error };
  state.messages.push(message); renderMessage(message); rememberConversation();
}
function renderMessage(message) {
  $('welcome').hidden = true;
  const item = document.createElement('article'); item.className = `message ${message.role}`;
  const head = document.createElement('div'); head.className = 'message-head';
  const avatar = document.createElement('span'); avatar.className = 'avatar'; avatar.textContent = message.role === 'user' ? 'Y' : message.tool ? 'T' : 'N';
  const label = document.createElement('span'); label.textContent = message.role === 'user' ? 'You' : message.tool ? 'Tool result' : 'Dynamic Neural Engine';
  head.append(avatar, label); const body = document.createElement('div'); body.className = 'message-body'; formatMessage(body, message.content);
  item.append(head, body);
  if (message.note) { const note = document.createElement('div'); note.className = 'message-note' + (message.error ? ' error-note' : ''); note.textContent = message.note; item.append(note); }
  $('messages').append(item); $('chatScroll').scrollTop = $('chatScroll').scrollHeight;
}
function rememberConversation() {
  try { localStorage.setItem('dne.chat.' + state.model + '|' + state.root, JSON.stringify(state.messages.slice(-40))); } catch { /* History is a convenience, not model storage. */ }
}
function loadConversation() {
  state.messages = []; $('messages').replaceChildren(); $('welcome').hidden = false;
  try { const saved = JSON.parse(localStorage.getItem('dne.chat.' + state.model + '|' + state.root) || '[]');
    for (const message of saved.slice(-40)) if (typeof message.content === 'string' && ['user', 'assistant'].includes(message.role)) { state.messages.push(message); renderMessage(message); }
  } catch { /* A fresh conversation still works. */ }
}
function toolText(result) {
  if (!result.ok) return result.error || 'The tool could not complete the request.';
  if (result.data?.claims) return `${result.text}\n\n${result.data.claims.map(claim => claim.text).join('\n')}\n\nSources:\n${result.data.sources.join('\n')}`;
  if (result.data?.code) return `${result.text}\n\n\`\`\`csharp\n${result.data.code}\`\`\`\n${result.data.training_cases} training cases · ${result.data.validation_cases} separate validation cases · ${result.data.candidates} candidates`;
  if (result.data?.results) return result.data.results.map(hit => `${hit.title}\n${hit.snippet}\n${hit.url}`).join('\n\n') || 'No search results.';
  return result.text || JSON.stringify(result.data ?? result, null, 2);
}
async function toolConversation(name, args) {
  const result = await callTool(name, args);
  appendMessage('assistant', toolText(result), { tool: true, error: !result.ok,
    note: result.ok ? name : 'No successful learning or command result was reported.' });
  $('activityPanel').hidden = false;
  return result;
}
async function operation(action, button) {
  if (state.busy) { toast('Finish or stop the current operation first.'); return; }
  setBusy(true); if (button) button.disabled = true;
  try { await action(); } catch (error) { toast(error.name === 'AbortError' ? 'Stopped.' : error.message, error.name !== 'AbortError'); }
  finally { setBusy(false); if (button) button.disabled = false; }
}
async function sendMessage(text) {
  await operation(async () => {
    showTab('chat'); const command = text.startsWith('/'); appendMessage('user', text, { tool: command });
    if (command) {
      if (text === '/help') { appendMessage('assistant', 'Choose a directory in the sidebar. Click a file to edit it. Research checks multiple sites; C# function learns from examples. I run the build myself after changing project code.\n\nCommands: /files [pattern], /read path, /search text, /web query, /learn-web topic, /build, /test, /skills, /model, /tool NAME JSON, /task JSON.\n\nThe brain is an experimental learner. General natural-language coding and AGI are not established.', { tool: true }); return; }
      if (text === '/files' || text.startsWith('/files ')) { await refreshFiles(text.length > 6 ? text.slice(7) : '*'); appendMessage('assistant', `${state.files.length} files listed in the sidebar.`, { tool: true }); return; }
      if (text.startsWith('/read ')) { await openFile(text.slice(6).replace(/^"|"$/g, '')); return; }
      if (text.startsWith('/search ')) { await toolConversation('workspace.search', { text: text.slice(8) }); return; }
      if (text.startsWith('/web ')) { await toolConversation('web.search', { query: text.slice(5) }); return; }
      if (text.startsWith('/learn-web ')) { await toolConversation('web.learn', { query: text.slice(11) }); return; }
      if (text === '/skills') { $('toolDialog').showModal(); return; }
      if (text === '/model') { const result = await api('/api/show', { model: state.model }, state.abort.signal); appendMessage('assistant', JSON.stringify(result, null, 2), { tool: true }); return; }
      if (text.startsWith('/synthesize ')) { await toolConversation('code.synthesize', JSON.parse(text.slice(12))); await refreshFiles(); return; }
      if (text.startsWith('/tool ')) { const call = text.slice(6); const split = call.indexOf(' '); await toolConversation(split < 0 ? call : call.slice(0, split), split < 0 ? {} : JSON.parse(call.slice(split + 1))); return; }
      if (text === '/build') { await toolConversation('workspace.run', { executable: 'dotnet', arguments: ['build'], timeout_ms: 120000 }); return; }
      if (text === '/test') { await toolConversation('workspace.run', { executable: 'dotnet', arguments: ['test'], timeout_ms: 120000 }); return; }
      if (text.startsWith('/task ')) {
        const task = JSON.parse(text.slice(6)); const start = performance.now();
        const result = await api('/api/agent/tasks', { ...task, ...scope() }, state.abort.signal);
        activity('agent.workflow', task, result, performance.now() - start); appendMessage('assistant', JSON.stringify(result, null, 2), { tool: true });
        await refreshFiles(); return;
      }
      throw new Error('Unknown command. Use /help or one of the tool buttons.');
    }
    const typing = document.createElement('div'); typing.className = 'typing'; typing.textContent = 'Working'; $('messages').append(typing);
    try {
      const messages = state.messages.filter(message => !message.tool).slice(-14).map(({ role, content }) => ({ role, content }));
      const result = await api('/api/chat', { model: state.model, messages, reset_context: messages.length === 1, stream: false,
        workspace_id: state.workspaceId || undefined, active_path: state.editor.path || undefined }, state.abort.signal);
      appendMessage('assistant', result.message.content || '(No learned response.)', {
        note: result.action ? (result.action.created ? 'Created ' + result.action.path : 'Updated ' + result.action.path)
          : result.learning_error ? 'Cross-check incomplete: ' + result.learning_error : result.tentative ? 'Best-effort: sources were limited or disagreed, so this is not cross-confirmed.' : result.learned_from_web ? 'Multi-site knowledge retained in the local brain.' : 'Local response',
        error: !!result.learning_error
      });
      if (result.action) {
        $('activityPanel').hidden = false;
        activity('assistant.action', { path: result.action.path }, { ok: true, data: result.action }, 0);
        await refreshFiles();
        await openFile(result.action.path).catch(() => {});
        await autoBuild();
      }
    } finally { typing.remove(); }
  });
}
async function autoBuild() {
  if (!state.workspaceId || !state.files.some(file => /\.(csproj|sln)$/i.test(file))) return;
  await toolConversation('workspace.run', { executable: 'dotnet', arguments: ['build'], timeout_ms: 120000 });
}
function unsavedOkay() { return !state.editor.dirty || confirm('Discard unsaved editor changes?'); }
async function selectWorkspace(path) {
  if (state.busy) throw new Error('Finish or stop the current operation before switching directories.');
  if (!unsavedOkay()) return;
  const result = await api('/api/ui/workspace', { path });
  rememberConversation();
  const changed = state.root !== result.root;
  state.workspaceId = result.workspace_id; state.root = result.root; $('workspacePath').value = result.root;
  if (changed) loadConversation();
  $('workspaceName').textContent = result.root.split(/[\\/]/).filter(Boolean).pop() || result.root;
  $('rootLabel').textContent = result.root; $('rootLabel').title = result.root;
  $('workspaceStatus').textContent = 'Selected · file operations stay in this directory.';
  state.editor = { path: '', hash: '', dirty: false, truncated: false }; $('editor').value = ''; $('editor').disabled = true;
  $('editorPath').textContent = 'No file selected'; $('editorMeta').textContent = 'Choose a file from the sidebar.'; updateDirty();
  try { localStorage.setItem('dne.workspace', result.root); } catch { }
  await refreshFiles();
}
async function refreshFiles(pattern = '*') {
  if (!state.workspaceId || !state.model) return;
  const selected = state.workspaceId;
  const data = ensureSuccess(await callTool('workspace.list', { pattern, limit: 1000 }));
  if (selected !== state.workspaceId) return;
  state.files = data.files; renderFiles();
}
function renderFiles() {
  const filter = $('fileFilter').value.toLowerCase(); $('files').replaceChildren();
  const files = state.files.filter(file => file.toLowerCase().includes(filter));
  for (const file of files) {
    const button = document.createElement('button'); button.className = 'file-item' + (file === state.editor.path ? ' selected' : '');
    button.textContent = file; button.title = file; button.addEventListener('click', () => openFile(file).catch(error => toast(error.message, true))); $('files').append(button);
  }
  if (!files.length) { const empty = document.createElement('div'); empty.className = 'file-empty'; empty.textContent = state.workspaceId ? 'No matching files. You can create a file with the + button.' : 'Choose a directory to get started.'; $('files').append(empty); }
}
async function openFile(path) {
  if (!unsavedOkay()) return;
  const selected = state.workspaceId;
  const data = ensureSuccess(await callTool('workspace.read', { path, max_chars: 200000 }));
  if (selected !== state.workspaceId) return;
  state.editor = { path: data.path, hash: data.sha256, dirty: false, truncated: data.truncated };
  $('editorPath').textContent = data.path; $('editor').value = data.content; $('editor').disabled = false; $('editor').readOnly = data.truncated;
  $('editorMeta').textContent = data.truncated ? 'Large file: truncated read-only preview. Use precise text replacements for edits.' : 'Changes are saved only when you choose Save.';
  updateDirty(); renderFiles(); showTab('editor');
  if (innerWidth < 850) $('sidebar').classList.add('mobile-hidden');
}
function updateDirty() { $('dirtyDot').textContent = state.editor.dirty ? '•' : ''; $('saveFile').disabled = !state.editor.path || !state.editor.dirty || state.editor.truncated; }
async function saveFile() {
  if (!state.editor.path || state.editor.truncated || !state.editor.dirty) return;
  const selected = state.workspaceId;
  const data = ensureSuccess(await callTool('workspace.write', { path: state.editor.path, content: $('editor').value, expected_hash: state.editor.hash }));
  if (selected !== state.workspaceId) return;
  state.editor.hash = data.sha256; state.editor.dirty = false; updateDirty(); $('activityPanel').hidden = false; toast('File saved. Diff available in Activity.');
}
async function loadModels(preferred) {
  const result = await api('/api/tags'); $('modelSelect').replaceChildren();
  for (const model of result.models) { const option = document.createElement('option'); option.value = model.name; option.textContent = model.name; $('modelSelect').append(option); }
  const chosen = result.models.some(model => model.name === preferred) ? preferred : result.models.some(model => model.name === 'current') ? 'current' : result.models[0]?.name || '';
  state.model = chosen; $('modelSelect').value = chosen; loadConversation();
  if (!chosen) toast('Create a model with the + button to get started.');
}
function cases(text, arity, required) {
  const values = text.split(/\r?\n/).map(line => line.trim()).filter(Boolean).map(line => {
    const match = /^(.+?)\s*=>\s*(-?\d+)$/.exec(line);
    if (!match) throw new Error('Use inputs => output, one case per line.');
    const inputs = match[1].split(',').map(value => Number(value.trim())); const output = Number(match[2]);
    if (inputs.length !== arity || !inputs.every(Number.isSafeInteger) || !Number.isSafeInteger(output)) throw new Error('Cases need the correct number of safe integer inputs and one integer output.');
    return { inputs, output };
  });
  if (required && values.length < 2) throw new Error('Provide at least two training cases.');
  return values;
}
function formHandler(formId, dialogId, action) {
  $(formId).addEventListener('submit', event => { event.preventDefault(); const form = event.currentTarget; operation(async () => { await action(new FormData(form)); $(dialogId).close(); }, form.querySelector('[type=submit]')); });
}
function showSkill() {
  const skill = state.skills.find(item => item.name === $('skillSelect').value);
  $('skillDescription').textContent = skill?.description || ''; $('skillSchema').textContent = JSON.stringify(skill?.parameters || {}, null, 2);
}

$('chatForm').addEventListener('submit', event => { event.preventDefault(); const value = $('prompt').value.trim(); if (!value || state.busy) return; $('prompt').value = ''; sendMessage(value); });
$('prompt').addEventListener('keydown', event => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); $('chatForm').requestSubmit(); } });
$('stopButton').addEventListener('click', () => state.abort?.abort());
$('chatTab').addEventListener('click', () => showTab('chat')); $('editorTab').addEventListener('click', () => showTab('editor'));
$('toggleActivity').addEventListener('click', () => $('activityPanel').hidden = !$('activityPanel').hidden);
$('closeActivity').addEventListener('click', () => $('activityPanel').hidden = true);
$('toggleSidebar').addEventListener('click', () => $('sidebar').classList.toggle('mobile-hidden'));
if (innerWidth < 850) $('sidebar').classList.add('mobile-hidden');
$('newChat').addEventListener('click', () => { state.messages = []; $('messages').replaceChildren(); $('welcome').hidden = false; rememberConversation(); showTab('chat'); });
$('fileFilter').addEventListener('input', renderFiles);
$('refreshFiles').addEventListener('click', () => refreshFiles().catch(error => toast(error.message, true)));
$('workspaceForm').addEventListener('submit', event => { event.preventDefault(); selectWorkspace($('workspacePath').value.trim()).catch(error => toast(error.message, true)); });
$('pickFolder').addEventListener('click', async () => {
  $('pickFolder').disabled = true; $('workspaceStatus').textContent = 'Choose a directory in the system dialog…';
  try { const result = await api('/api/ui/pick-directory', {}); if (result.selected) await selectWorkspace(result.path); }
  catch (error) { toast(error.message, true); }
  finally { $('pickFolder').disabled = state.busy || !state.config.native_picker; $('workspaceStatus').textContent = state.root ? 'Selected · file operations stay in this directory.' : 'Enter a path to choose a workspace.'; }
});
$('editor').addEventListener('input', () => { state.editor.dirty = true; updateDirty(); });
$('editor').addEventListener('keydown', event => { if (event.key === 'Tab' && !$('editor').readOnly) { event.preventDefault(); $('editor').setRangeText('  ', $('editor').selectionStart, $('editor').selectionEnd, 'end'); state.editor.dirty = true; updateDirty(); } });
$('saveFile').addEventListener('click', () => saveFile().catch(error => toast(error.message, true)));
document.addEventListener('keydown', event => { if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's' && !$('editorView').hidden) { event.preventDefault(); saveFile().catch(error => toast(error.message, true)); } });
window.addEventListener('beforeunload', event => { if (state.editor.dirty) { event.preventDefault(); event.returnValue = ''; } });
for (const button of document.querySelectorAll('[data-close]')) button.addEventListener('click', () => button.closest('dialog').close());
for (const button of document.querySelectorAll('[data-prompt]')) button.addEventListener('click', () => sendMessage(button.dataset.prompt));
for (const id of ['researchButton', 'welcomeResearch']) $(id).addEventListener('click', () => $('researchDialog').showModal());
for (const id of ['codeButton', 'welcomeCode']) $(id).addEventListener('click', () => $('codeDialog').showModal());
$('toolsButton').addEventListener('click', () => $('toolDialog').showModal());
$('newModel').addEventListener('click', () => $('modelDialog').showModal()); $('newFile').addEventListener('click', () => { if (!state.workspaceId) toast('Choose a workspace first.'); else $('fileDialog').showModal(); });
$('skillSelect').addEventListener('change', showSkill);
$('modelSelect').addEventListener('change', () => { state.model = $('modelSelect').value; try { localStorage.setItem('dne.model', state.model); } catch { } loadConversation(); refreshFiles().catch(error => toast(error.message, true)); });
formHandler('researchForm', 'researchDialog', async data => { showTab('chat'); const args = { query: data.get('query'), min_sources: Number(data.get('minimum')), max_sites: 6 }; if (data.get('question').trim()) args.question = data.get('question').trim(); await toolConversation('web.learn', args); });
formHandler('codeForm', 'codeDialog', async data => {
  const variables = data.get('variables').split(',').map(value => value.trim()).filter(Boolean);
  const args = { name: data.get('name'), variables, examples: cases(data.get('examples'), variables.length, true), validation: cases(data.get('validation'), variables.length, false) };
  if (data.get('path').trim()) args.path = data.get('path').trim(); showTab('chat'); await toolConversation('code.synthesize', args); await refreshFiles();
});
formHandler('toolForm', 'toolDialog', async data => { showTab('chat'); await toolConversation(data.get('skill'), JSON.parse(data.get('arguments'))); await refreshFiles(); });
formHandler('modelForm', 'modelDialog', async data => { await api('/api/ui/models', { name: data.get('name') }); await loadModels(data.get('name')); await refreshFiles(); toast('Local model created.'); });
formHandler('fileForm', 'fileDialog', async data => { const result = await callTool('workspace.write', { path: data.get('path'), content: data.get('content'), create_only: true }); ensureSuccess(result); await refreshFiles(); await openFile(data.get('path')); });

(async function initialize() {
  try {
    const [config, skills] = await Promise.all([api('/api/ui/config'), api('/api/skills')]); state.config = config; state.skills = skills.skills;
    $('webStatus').textContent = config.live_web_learning ? 'Web learning on' : 'Local inference';
    $('pickFolder').disabled = !config.native_picker;
    for (const skill of state.skills) { const option = document.createElement('option'); option.value = skill.name; option.textContent = skill.name; $('skillSelect').append(option); } showSkill();
    let model, path; try { model = localStorage.getItem('dne.model'); path = localStorage.getItem('dne.workspace'); } catch { }
    await loadModels(model); path ||= config.workspace; $('workspacePath').value = path;
    try { await selectWorkspace(path); } catch (error) { toast('Choose a development directory: ' + error.message, true); }
    renderFiles();
  } catch (error) { toast('Could not initialize the app: ' + error.message, true); }
})();
