'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const readline = require('node:readline');
const { spawn } = require('node:child_process');
const test = require('node:test');

const adapterPath = path.resolve(__dirname, '..', 'unity-mcp-stdio.js');

test('exposes read tools and one confirmation-gated mutation workflow', async (context) => {
  const projectPath = fs.mkdtempSync(path.join(os.tmpdir(), 'guildidle-unity-mcp-'));
  context.after(() => fs.rmSync(projectPath, { recursive: true, force: true }));

  const token = 'test-token-with-at-least-32-characters';
  const requests = [];
  const server = http.createServer((request, response) => {
    let body = '';
    request.setEncoding('utf8');
    request.on('data', (chunk) => { body += chunk; });
    request.on('end', () => {
      assert.equal(request.method, 'POST');
      assert.equal(request.url, '/invoke');
      assert.equal(request.headers['x-unity-mcp-token'], token);
      const parsed = JSON.parse(body);
      requests.push(parsed);
      let result = { unityVersion: '6000.3.12f1', bridgeMode: 'confirmation-gated' };
      if (parsed.tool === 'unity_plan_create_ui_object') {
        result = {
          planToken: 'test-plan-token-with-32-characters',
          expiresInSeconds: 90,
          testOnly: true,
        };
      } else if (parsed.tool === 'unity_apply_create_ui_object') {
        result = {
          applied: true,
          tokenConsumed: true,
          testOnly: true,
          destroyedAfterValidation: true,
          saved: false,
          sceneDirty: false,
        };
      }
      response.writeHead(200, { 'Content-Type': 'application/json' });
      response.end(JSON.stringify({
        ok: true,
        resultJson: JSON.stringify(result),
      }));
    });
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });
  context.after(() => server.close());

  const descriptorDirectory = path.join(projectPath, 'Library', 'UnityMcp');
  fs.mkdirSync(descriptorDirectory, { recursive: true });
  fs.writeFileSync(path.join(descriptorDirectory, 'bridge.json'), JSON.stringify({
    protocolVersion: 2,
    projectPath,
    processId: process.pid,
    host: '127.0.0.1',
    port: server.address().port,
    token,
    mode: 'confirmation-gated',
  }));

  const child = spawn(process.execPath, [adapterPath], {
    env: { ...process.env, GUILDIDLE_UNITY_PROJECT: projectPath },
    stdio: ['pipe', 'pipe', 'pipe'],
  });
  context.after(() => child.kill());

  let stderr = '';
  child.stderr.setEncoding('utf8');
  child.stderr.on('data', (chunk) => { stderr += chunk; });
  const responses = new Map();
  const waiters = new Map();
  const lines = readline.createInterface({ input: child.stdout });
  lines.on('line', (line) => {
    const value = JSON.parse(line);
    const waiter = waiters.get(value.id);
    if (waiter) {
      waiters.delete(value.id);
      waiter.resolve(value);
    } else {
      responses.set(value.id, value);
    }
  });

  const call = (message) => new Promise((resolve, reject) => {
    const existing = responses.get(message.id);
    if (existing) {
      responses.delete(message.id);
      resolve(existing);
      return;
    }
    const timeout = setTimeout(() => {
      waiters.delete(message.id);
      reject(new Error(`Timed out waiting for ${message.method}. stderr: ${stderr}`));
    }, 5000);
    waiters.set(message.id, {
      resolve: (value) => {
        clearTimeout(timeout);
        resolve(value);
      },
    });
    child.stdin.write(`${JSON.stringify(message)}\n`);
  });

  const initialized = await call({
    jsonrpc: '2.0',
    id: 1,
    method: 'initialize',
    params: {
      protocolVersion: '2025-06-18',
      capabilities: {},
      clientInfo: { name: 'test', version: '1.0.0' },
    },
  });
  assert.equal(initialized.result.protocolVersion, '2025-06-18');
  assert.equal(initialized.result.serverInfo.name, 'guildidle-unity');
  assert.match(initialized.result.instructions, /unexpired token/i);

  const listed = await call({ jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} });
  assert.deepEqual(listed.result.tools.map((tool) => tool.name), [
    'unity_editor_status',
    'unity_selection',
    'unity_find_assets',
    'unity_scene_hierarchy',
    'unity_inspect_prefab',
    'unity_plan_create_ui_object',
    'unity_apply_create_ui_object',
  ]);
  for (const tool of listed.result.tools.slice(0, 5)) {
    assert.equal(tool.annotations.readOnlyHint, true);
    assert.equal(tool.annotations.destructiveHint, false);
    assert.equal(tool.annotations.idempotentHint, true);
    assert.equal(tool.annotations.openWorldHint, false);
  }
  const planningTool = listed.result.tools[5];
  assert.equal(planningTool.annotations.readOnlyHint, true);
  assert.equal(planningTool.annotations.idempotentHint, false);
  const applyTool = listed.result.tools[6];
  assert.equal(applyTool.annotations.readOnlyHint, false);
  assert.equal(applyTool.annotations.destructiveHint, false);
  assert.equal(applyTool.annotations.idempotentHint, false);
  assert.equal(applyTool.annotations.openWorldHint, false);

  const modernList = await call({
    jsonrpc: '2.0',
    id: 4,
    method: 'tools/list',
    params: {
      _meta: {
        'io.modelcontextprotocol/protocolVersion': '2026-07-28',
        'io.modelcontextprotocol/clientInfo': { name: 'test', version: '1.0.0' },
        'io.modelcontextprotocol/clientCapabilities': {},
      },
    },
  });
  assert.equal(modernList.result.resultType, 'complete');

  const called = await call({
    jsonrpc: '2.0',
    id: 3,
    method: 'tools/call',
    params: { name: 'unity_editor_status', arguments: {} },
  });
  assert.equal(called.result.isError, false);
  assert.equal(called.result.structuredContent.unityVersion, '6000.3.12f1');
  assert.deepEqual(JSON.parse(called.result.content[0].text), called.result.structuredContent);
  assert.equal(requests.length, 1);
  assert.equal(requests[0].tool, 'unity_editor_status');
  assert.equal(requests[0].argumentsJson, '{}');

  const planned = await call({
    jsonrpc: '2.0',
    id: 5,
    method: 'tools/call',
    params: {
      name: 'unity_plan_create_ui_object',
      arguments: {
        scene_path: 'Assets/Scenes/Init.unity',
        parent_path: 'UIRoot/Overlays',
        object_name: 'TransientValidation',
        save_scene: false,
        test_only: true,
      },
    },
  });
  assert.equal(planned.result.isError, false);
  assert.equal(planned.result.structuredContent.expiresInSeconds, 90);

  const applied = await call({
    jsonrpc: '2.0',
    id: 6,
    method: 'tools/call',
    params: {
      name: 'unity_apply_create_ui_object',
      arguments: {
        plan_token: planned.result.structuredContent.planToken,
        confirmation: 'APPLY',
      },
    },
  });
  assert.equal(applied.result.isError, false);
  assert.equal(applied.result.structuredContent.destroyedAfterValidation, true);
  assert.equal(requests.length, 3);
  assert.equal(requests[1].tool, 'unity_plan_create_ui_object');
  assert.match(requests[1].argumentsJson, /"testOnly":true/);
  assert.equal(requests[2].tool, 'unity_apply_create_ui_object');
  assert.match(requests[2].argumentsJson, /"confirmation":"APPLY"/);
});
