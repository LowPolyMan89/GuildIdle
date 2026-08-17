#!/usr/bin/env node
'use strict';

const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const readline = require('node:readline');

const SERVER_NAME = 'guildidle-unity';
const SERVER_VERSION = '0.2.0';
const DEFAULT_PROTOCOL_VERSION = '2025-06-18';
const SUPPORTED_INITIALIZE_PROTOCOLS = new Set([
  '2024-11-05',
  '2025-03-26',
  '2025-06-18',
  '2025-11-25',
  '2026-07-28',
]);
const PROJECT_ROOT = path.resolve(
  process.env.GUILDIDLE_UNITY_PROJECT || path.join(__dirname, '..', '..'),
);
const DESCRIPTOR_PATH = path.join(PROJECT_ROOT, 'Library', 'UnityMcp', 'bridge.json');
const REQUEST_TIMEOUT_MS = 25_000;

const readOnlyAnnotations = Object.freeze({
  readOnlyHint: true,
  destructiveHint: false,
  idempotentHint: true,
  openWorldHint: false,
});
const planningAnnotations = Object.freeze({
  readOnlyHint: true,
  destructiveHint: false,
  idempotentHint: false,
  openWorldHint: false,
});
const guardedWriteAnnotations = Object.freeze({
  readOnlyHint: false,
  destructiveHint: false,
  idempotentHint: false,
  openWorldHint: false,
});

const tools = Object.freeze([
  {
    name: 'unity_editor_status',
    title: 'Unity Editor Status',
    description: 'Read Unity version, play/compile/update state, active build target, active scene, and selection count.',
    inputSchema: { type: 'object', additionalProperties: false },
    annotations: readOnlyAnnotations,
  },
  {
    name: 'unity_selection',
    title: 'Unity Editor Selection',
    description: 'Read the current Unity Editor selection, including asset paths and hierarchy paths when available.',
    inputSchema: { type: 'object', additionalProperties: false },
    annotations: readOnlyAnnotations,
  },
  {
    name: 'unity_find_assets',
    title: 'Find Unity Assets',
    description: 'Run a bounded AssetDatabase search. Optional type is a Unity type name; folders must be under Assets/.',
    inputSchema: {
      type: 'object',
      additionalProperties: false,
      properties: {
        query: { type: 'string', maxLength: 200, description: 'Unity asset search text; empty lists assets.' },
        type: { type: 'string', pattern: '^[A-Za-z0-9_.]+$', description: 'Optional Unity main asset type, such as Prefab or Scene.' },
        folders: {
          type: 'array',
          maxItems: 20,
          items: { type: 'string', pattern: '^Assets(?:/.*)?$' },
          description: 'Optional project folders to search.',
        },
        limit: { type: 'integer', minimum: 1, maximum: 500, default: 100 },
      },
    },
    annotations: readOnlyAnnotations,
  },
  {
    name: 'unity_scene_hierarchy',
    title: 'Read Loaded Scene Hierarchy',
    description: 'Read a bounded flattened hierarchy from the active scene or another scene that is already loaded. Never opens a scene.',
    inputSchema: {
      type: 'object',
      additionalProperties: false,
      properties: {
        scene_path: { type: 'string', description: 'Optional loaded scene asset path or scene name.' },
        max_depth: { type: 'integer', minimum: 0, maximum: 20, default: 8 },
        max_nodes: { type: 'integer', minimum: 1, maximum: 1000, default: 250 },
        include_inactive: { type: 'boolean', default: false },
        include_components: { type: 'boolean', default: false },
      },
    },
    annotations: readOnlyAnnotations,
  },
  {
    name: 'unity_inspect_prefab',
    title: 'Inspect Unity Prefab',
    description: 'Inspect a prefab asset hierarchy and its component/script summary without opening Prefab Mode or changing the asset.',
    inputSchema: {
      type: 'object',
      additionalProperties: false,
      properties: {
        asset_path: { type: 'string', pattern: '^Assets/.*\\.prefab$', description: 'Prefab path under Assets/.' },
        max_depth: { type: 'integer', minimum: 0, maximum: 20, default: 10 },
        max_nodes: { type: 'integer', minimum: 1, maximum: 1000, default: 300 },
      },
      required: ['asset_path'],
    },
    annotations: readOnlyAnnotations,
  },
  {
    name: 'unity_plan_create_ui_object',
    title: 'Preview UI Object Creation',
    description: 'Read-only preview for one exact RectTransform GameObject addition in an already-loaded scene. Returns a one-time 90-second plan token; never changes Unity content.',
    inputSchema: {
      type: 'object',
      additionalProperties: false,
      properties: {
        scene_path: { type: 'string', pattern: '^Assets/.*\\.unity$', description: 'Exact loaded scene asset path.' },
        parent_path: { type: 'string', minLength: 1, maxLength: 512, description: 'Exact slash-separated hierarchy path to an existing parent.' },
        object_name: { type: 'string', pattern: '^[A-Za-z][A-Za-z0-9 _-]{0,63}$' },
        save_scene: { type: 'boolean', default: false, description: 'If true, the later apply may save only this exact scene, and only if it is clean.' },
        test_only: { type: 'boolean', default: false, description: 'If true, apply validates a hidden transient object and destroys it without touching the scene.' },
      },
      required: ['scene_path', 'parent_path', 'object_name'],
    },
    annotations: planningAnnotations,
  },
  {
    name: 'unity_apply_create_ui_object',
    title: 'Confirm UI Object Creation',
    description: 'Approval-gated write. Consumes one exact unexpired plan token and requires confirmation=APPLY. Creates only the planned RectTransform object; no generic mutation or execution.',
    inputSchema: {
      type: 'object',
      additionalProperties: false,
      properties: {
        plan_token: {
          type: 'string',
          minLength: 20,
          maxLength: 128,
          description: 'Exact short-lived token returned by unity_plan_create_ui_object.',
        },
        confirmation: {
          type: 'string',
          enum: ['APPLY'],
          description: 'Must be exactly APPLY after the user reviews the plan.',
        },
      },
      required: ['plan_token', 'confirmation'],
    },
    annotations: guardedWriteAnnotations,
  },
]);

const toolNames = new Set(tools.map((tool) => tool.name));
let negotiatedProtocol = DEFAULT_PROTOCOL_VERSION;

function writeMessage(message) {
  process.stdout.write(`${JSON.stringify(message)}\n`);
}

function protocolError(id, code, message, data) {
  const error = { code, message };
  if (data !== undefined) error.data = data;
  return { jsonrpc: '2.0', id: id ?? null, error };
}

function isModernRequest(message) {
  const version = message?.params?._meta?.['io.modelcontextprotocol/protocolVersion'];
  return version === '2026-07-28' || negotiatedProtocol === '2026-07-28';
}

function completeResult(value, modern) {
  return modern ? { resultType: 'complete', ...value } : value;
}

function mapArguments(toolName, supplied) {
  const input = supplied && typeof supplied === 'object' && !Array.isArray(supplied) ? supplied : {};
  switch (toolName) {
    case 'unity_editor_status':
    case 'unity_selection':
      return {};
    case 'unity_find_assets':
      return {
        query: input.query ?? '',
        type: input.type ?? '',
        folders: input.folders ?? [],
        limit: input.limit ?? 100,
      };
    case 'unity_scene_hierarchy':
      return {
        scenePath: input.scene_path ?? '',
        maxDepth: input.max_depth ?? 8,
        maxNodes: input.max_nodes ?? 250,
        includeInactive: input.include_inactive ?? false,
        includeComponents: input.include_components ?? false,
      };
    case 'unity_inspect_prefab':
      return {
        assetPath: input.asset_path ?? '',
        maxDepth: input.max_depth ?? 10,
        maxNodes: input.max_nodes ?? 300,
      };
    case 'unity_plan_create_ui_object':
      return {
        scenePath: input.scene_path ?? '',
        parentPath: input.parent_path ?? '',
        objectName: input.object_name ?? '',
        saveScene: input.save_scene ?? false,
        testOnly: input.test_only ?? false,
      };
    case 'unity_apply_create_ui_object':
      return {
        planToken: input.plan_token ?? '',
        confirmation: input.confirmation ?? '',
      };
    default:
      throw new Error(`Unknown tool '${toolName}'.`);
  }
}

function loadDescriptor() {
  let descriptor;
  try {
    descriptor = JSON.parse(fs.readFileSync(DESCRIPTOR_PATH, 'utf8'));
  } catch (error) {
    throw new Error(
      `Unity Editor bridge is unavailable. Open GuildIdle in Unity and wait for scripts to compile. (${error.message})`,
    );
  }

  if (descriptor.protocolVersion !== 2 || descriptor.mode !== 'confirmation-gated') {
    throw new Error('Unity Editor bridge descriptor is incompatible or not confirmation-gated.');
  }
  if (descriptor.host !== '127.0.0.1') {
    throw new Error('Unity Editor bridge refused: only 127.0.0.1 is allowed.');
  }
  if (!Number.isInteger(descriptor.port) || descriptor.port < 1024 || descriptor.port > 65535) {
    throw new Error('Unity Editor bridge descriptor contains an invalid port.');
  }
  if (typeof descriptor.token !== 'string' || descriptor.token.length < 32) {
    throw new Error('Unity Editor bridge descriptor contains an invalid token.');
  }
  return descriptor;
}

function invokeUnity(toolName, args) {
  const descriptor = loadDescriptor();
  const body = JSON.stringify({ tool: toolName, argumentsJson: JSON.stringify(args) });
  return new Promise((resolve, reject) => {
    const request = http.request({
      host: '127.0.0.1',
      port: descriptor.port,
      path: '/invoke',
      method: 'POST',
      headers: {
        'Content-Type': 'application/json; charset=utf-8',
        'Content-Length': Buffer.byteLength(body),
        'X-Unity-MCP-Token': descriptor.token,
      },
      timeout: REQUEST_TIMEOUT_MS,
    }, (response) => {
      let responseBody = '';
      response.setEncoding('utf8');
      response.on('data', (chunk) => {
        responseBody += chunk;
        if (responseBody.length > 8 * 1024 * 1024) request.destroy(new Error('Unity bridge response is too large.'));
      });
      response.on('end', () => {
        try {
          const parsed = JSON.parse(responseBody);
          if (response.statusCode !== 200 || parsed.ok !== true) {
            reject(new Error(parsed.error || `Unity bridge returned HTTP ${response.statusCode}.`));
            return;
          }
          resolve(JSON.parse(parsed.resultJson));
        } catch (error) {
          reject(new Error(`Invalid Unity bridge response: ${error.message}`));
        }
      });
    });
    request.on('timeout', () => request.destroy(new Error('Unity bridge request timed out.')));
    request.on('error', reject);
    request.end(body);
  });
}

async function handleRequest(message) {
  if (!message || message.jsonrpc !== '2.0' || typeof message.method !== 'string') {
    if (message?.id !== undefined) writeMessage(protocolError(message.id, -32600, 'Invalid Request'));
    return;
  }

  const hasId = message.id !== undefined;
  if (!hasId) return;

  switch (message.method) {
    case 'initialize': {
      const requested = message.params?.protocolVersion;
      negotiatedProtocol = typeof requested === 'string' && SUPPORTED_INITIALIZE_PROTOCOLS.has(requested)
        ? requested
        : DEFAULT_PROTOCOL_VERSION;
      writeMessage({
        jsonrpc: '2.0',
        id: message.id,
        result: {
          protocolVersion: negotiatedProtocol,
          capabilities: { tools: { listChanged: false } },
          serverInfo: { name: SERVER_NAME, title: 'GuildIdle Unity Editor (confirmation-gated)', version: SERVER_VERSION },
          instructions: 'Unity reads are safe. The only write is a bounded UI GameObject addition: first call unity_plan_create_ui_object, show the exact plan to the user, then call unity_apply_create_ui_object with its unexpired token and confirmation=APPLY. The apply tool requires host approval. Never apply without deliberate user confirmation.',
        },
      });
      return;
    }
    case 'ping':
      writeMessage({ jsonrpc: '2.0', id: message.id, result: {} });
      return;
    case 'tools/list':
      writeMessage({
        jsonrpc: '2.0',
        id: message.id,
        result: completeResult({ tools }, isModernRequest(message)),
      });
      return;
    case 'tools/call': {
      const toolName = message.params?.name;
      if (!toolNames.has(toolName)) {
        writeMessage(protocolError(message.id, -32602, `Unknown tool '${toolName ?? ''}'.`));
        return;
      }
      try {
        const args = mapArguments(toolName, message.params?.arguments);
        const result = await invokeUnity(toolName, args);
        writeMessage({
          jsonrpc: '2.0',
          id: message.id,
          result: completeResult({
            content: [{ type: 'text', text: JSON.stringify(result) }],
            structuredContent: result,
            isError: false,
          }, isModernRequest(message)),
        });
      } catch (error) {
        writeMessage({
          jsonrpc: '2.0',
          id: message.id,
          result: completeResult({
            content: [{ type: 'text', text: error.message }],
            isError: true,
          }, isModernRequest(message)),
        });
      }
      return;
    }
    default:
      writeMessage(protocolError(message.id, -32601, `Method not found: ${message.method}`));
  }
}

const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
input.on('line', (line) => {
  if (!line.trim()) return;
  let message;
  try {
    message = JSON.parse(line);
  } catch (error) {
    writeMessage(protocolError(null, -32700, 'Parse error', error.message));
    return;
  }
  handleRequest(message).catch((error) => {
    if (message?.id !== undefined) writeMessage(protocolError(message.id, -32603, 'Internal error', error.message));
  });
});
