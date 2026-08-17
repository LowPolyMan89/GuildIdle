using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GuildIdle.EditorTools.Mcp
{
    [InitializeOnLoad]
    internal static class UnityMcpBridge
    {
        private const int DefaultPort = 8963;
        private const int MaxRequestBytes = 1024 * 1024;
        private const int MaxHeaderBytes = 16 * 1024;
        private const int RequestTimeoutSeconds = 20;
        private const int MutationPlanTtlSeconds = 90;
        private const int MaxMutationPlans = 32;
        private const string TokenHeader = "X-Unity-MCP-Token";

        private static readonly ConcurrentQueue<PendingRequest> PendingRequests =
            new ConcurrentQueue<PendingRequest>();
        private static readonly Dictionary<string, CreateUiObjectPlan> CreateUiObjectPlans =
            new Dictionary<string, CreateUiObjectPlan>(StringComparer.Ordinal);

        private static TcpListener _listener;
        private static Thread _listenerThread;
        private static volatile bool _running;
        private static string _token;
        private static string _descriptorPath;
        private static int _port;

        static UnityMcpBridge()
        {
            EditorApplication.update += ProcessPendingRequests;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;

            if (!Application.isBatchMode)
                Start();
        }

        private static void Start()
        {
            if (_running)
                return;

            _port = ResolvePort();
            _token = CreateToken();
            _descriptorPath = Path.Combine(ProjectRoot, "Library", "UnityMcp", "bridge.json");

            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start(8);
                _running = true;
                WriteDescriptor();

                _listenerThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = "GuildIdle Unity MCP bridge"
                };
                _listenerThread.Start();
                Debug.Log($"[UnityMcpBridge] Confirmation-gated bridge listening on 127.0.0.1:{_port}.");
            }
            catch (Exception exception)
            {
                _running = false;
                TryDeleteDescriptor();
                _listener?.Stop();
                _listener = null;
                Debug.LogError($"[UnityMcpBridge] Failed to start confirmation-gated bridge: {exception.Message}");
            }
        }

        private static void Stop()
        {
            if (!_running && _listener == null)
                return;

            _running = false;
            try
            {
                _listener?.Stop();
            }
            catch (SocketException)
            {
                // Closing the listener is expected to interrupt AcceptTcpClient.
            }

            _listener = null;
            TryDeleteDescriptor();

            while (PendingRequests.TryDequeue(out var pending))
            {
                pending.ResponseJson = ErrorResponse("Unity bridge stopped before the request completed.");
                pending.Completed.Set();
            }
        }

        private static void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    using var client = _listener.AcceptTcpClient();
                    client.NoDelay = true;
                    client.ReceiveTimeout = RequestTimeoutSeconds * 1000;
                    client.SendTimeout = RequestTimeoutSeconds * 1000;
                    ProcessClient(client);
                }
                catch (SocketException)
                {
                    if (_running)
                        Thread.Sleep(100);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception)
                {
                    if (_running)
                        Thread.Sleep(100);
                }
            }
        }

        private static void ProcessClient(TcpClient client)
        {
            if (!(client.Client.RemoteEndPoint is IPEndPoint remote) || !IPAddress.IsLoopback(remote.Address))
                return;

            var stream = client.GetStream();
            HttpRequest request;
            try
            {
                request = ReadRequest(stream);
            }
            catch (Exception exception)
            {
                WriteResponse(stream, 400, "Bad Request", ErrorResponse(exception.Message));
                return;
            }

            if (request.Method == "GET" && request.Path == "/health")
            {
                WriteResponse(stream, 200, "OK", "{\"ok\":true,\"mode\":\"read-only\"}");
                return;
            }

            if (request.Method != "POST" || request.Path != "/invoke")
            {
                WriteResponse(stream, 404, "Not Found", ErrorResponse("Unknown endpoint."));
                return;
            }

            if (!request.Headers.TryGetValue(TokenHeader, out var suppliedToken) ||
                !string.Equals(suppliedToken, _token, StringComparison.Ordinal))
            {
                WriteResponse(stream, 401, "Unauthorized", ErrorResponse("Invalid bridge token."));
                return;
            }

            var pending = new PendingRequest(request.Body);
            PendingRequests.Enqueue(pending);
            if (!pending.Completed.Wait(TimeSpan.FromSeconds(RequestTimeoutSeconds)))
            {
                WriteResponse(stream, 503, "Service Unavailable", ErrorResponse("Unity Editor did not process the request in time."));
                return;
            }

            WriteResponse(stream, 200, "OK", pending.ResponseJson);
        }

        private static void ProcessPendingRequests()
        {
            var processed = 0;
            while (processed < 16 && PendingRequests.TryDequeue(out var pending))
            {
                try
                {
                    var request = JsonUtility.FromJson<BridgeRequest>(pending.RequestJson);
                    if (request == null || string.IsNullOrWhiteSpace(request.tool))
                        throw new ArgumentException("A tool name is required.");

                    var resultJson = ExecuteTool(request.tool, request.argumentsJson);
                    pending.ResponseJson = SuccessResponse(resultJson);
                }
                catch (Exception exception)
                {
                    pending.ResponseJson = ErrorResponse(exception.Message);
                }
                finally
                {
                    pending.Completed.Set();
                }

                processed++;
            }
        }

        private static string ExecuteTool(string tool, string argumentsJson)
        {
            var arguments = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
            return tool switch
            {
                "unity_editor_status" => JsonUtility.ToJson(GetEditorStatus(), true),
                "unity_selection" => JsonUtility.ToJson(GetSelection(), true),
                "unity_find_assets" => JsonUtility.ToJson(FindAssets(JsonUtility.FromJson<FindAssetsArguments>(arguments)), true),
                "unity_scene_hierarchy" => JsonUtility.ToJson(GetSceneHierarchy(JsonUtility.FromJson<SceneHierarchyArguments>(arguments)), true),
                "unity_inspect_prefab" => JsonUtility.ToJson(InspectPrefab(JsonUtility.FromJson<InspectPrefabArguments>(arguments)), true),
                "unity_plan_create_ui_object" => JsonUtility.ToJson(PlanCreateUiObject(JsonUtility.FromJson<PlanCreateUiObjectArguments>(arguments)), true),
                "unity_apply_create_ui_object" => JsonUtility.ToJson(ApplyCreateUiObject(JsonUtility.FromJson<ApplyCreateUiObjectArguments>(arguments)), true),
                _ => throw new ArgumentException($"Unknown tool '{tool}'.")
            };
        }

        private static CreateUiObjectPlanResult PlanCreateUiObject(PlanCreateUiObjectArguments arguments)
        {
            EnsureEditorAllowsMutation();
            if (arguments == null)
                throw new ArgumentException("Planning arguments are required.");

            var scenePath = NormalizeAssetPath(arguments.scenePath);
            if (!scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("scene_path must reference a .unity asset under Assets/.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                throw new FileNotFoundException($"Scene asset was not found at '{scenePath}'.");

            var scene = ResolveLoadedSceneForMutation(scenePath);
            var parentPath = ValidateHierarchyPath(arguments.parentPath);
            var parent = ResolveHierarchyPath(scene, parentPath);
            var objectName = ValidateUiObjectName(arguments.objectName);
            RejectExistingChild(parent, objectName);

            if (arguments.testOnly && arguments.saveScene)
                throw new ArgumentException("test_only and save_scene cannot both be true.");
            if (arguments.saveScene && scene.isDirty)
                throw new InvalidOperationException("The named scene is already dirty. Refusing to save unrelated changes.");
            if (arguments.saveScene && !AssetDatabase.IsOpenForEdit(scenePath))
                throw new InvalidOperationException($"Scene '{scenePath}' is not open for edit.");

            PruneMutationPlans();
            while (CreateUiObjectPlans.Count >= MaxMutationPlans)
                RemoveOldestMutationPlan();

            var now = DateTime.UtcNow;
            var plan = new CreateUiObjectPlan
            {
                Token = CreatePlanToken(),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddSeconds(MutationPlanTtlSeconds),
                ScenePath = scenePath,
                ParentPath = parentPath,
                ParentGlobalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(parent.gameObject).ToString(),
                ParentChildSignature = GetDirectChildSignature(parent),
                ObjectName = objectName,
                SaveScene = arguments.saveScene,
                TestOnly = arguments.testOnly,
                SceneWasDirty = scene.isDirty
            };
            CreateUiObjectPlans.Add(plan.Token, plan);

            return new CreateUiObjectPlanResult
            {
                operation = "create_ui_object",
                planToken = plan.Token,
                expiresAtUtc = plan.ExpiresAtUtc.ToString("O"),
                expiresInSeconds = MutationPlanTtlSeconds,
                scopeKind = "loaded_scene",
                scopePath = scenePath,
                parentPath = parentPath,
                parentGlobalObjectId = plan.ParentGlobalObjectId,
                objectName = objectName,
                rectTransform = DefaultRectTransform(),
                saveScene = plan.SaveScene,
                testOnly = plan.TestOnly,
                sceneDirtyAtPlan = plan.SceneWasDirty,
                plannedChange = plan.TestOnly
                    ? "Validate construction of a transient RectTransform object, then destroy it without touching the scene."
                    : $"Create one RectTransform GameObject named '{objectName}' under '{parentPath}'.",
                confirmationRequired = "Call unity_apply_create_ui_object with this exact plan_token and confirmation='APPLY'."
            };
        }

        private static CreateUiObjectApplyResult ApplyCreateUiObject(ApplyCreateUiObjectArguments arguments)
        {
            EnsureEditorAllowsMutation();
            if (arguments == null)
                throw new ArgumentException("Apply arguments are required.");
            if (!string.Equals(arguments.confirmation, "APPLY", StringComparison.Ordinal))
                throw new ArgumentException("confirmation must be exactly 'APPLY'.");

            var token = (arguments.planToken ?? string.Empty).Trim();
            if (token.Length < 20 || token.Length > 128 || Regex.IsMatch(token, "\\s"))
                throw new ArgumentException("plan_token has an invalid format.");

            PruneMutationPlans();
            if (!CreateUiObjectPlans.TryGetValue(token, out var plan))
                throw new InvalidOperationException("Plan token is invalid, expired, already used, or belongs to a previous Unity domain session.");
            CreateUiObjectPlans.Remove(token);

            if (DateTime.UtcNow > plan.ExpiresAtUtc)
                throw new InvalidOperationException("Plan token has expired. Create a new preview plan.");

            var scene = ResolveLoadedSceneForMutation(plan.ScenePath);
            if (scene.isDirty != plan.SceneWasDirty)
                throw new InvalidOperationException("The scene dirty state changed after planning. Create a new preview plan.");
            if (plan.SaveScene && scene.isDirty)
                throw new InvalidOperationException("The named scene is dirty. Refusing to save unrelated changes.");
            if (plan.SaveScene && !AssetDatabase.IsOpenForEdit(plan.ScenePath))
                throw new InvalidOperationException($"Scene '{plan.ScenePath}' is not open for edit.");

            var parent = ResolveHierarchyPath(scene, plan.ParentPath);
            var currentParentId = GlobalObjectId.GetGlobalObjectIdSlow(parent.gameObject).ToString();
            if (!string.Equals(currentParentId, plan.ParentGlobalObjectId, StringComparison.Ordinal))
                throw new InvalidOperationException("The planned parent identity changed. Create a new preview plan.");
            if (!string.Equals(GetDirectChildSignature(parent), plan.ParentChildSignature, StringComparison.Ordinal))
                throw new InvalidOperationException("The planned parent's direct children changed. Create a new preview plan.");
            RejectExistingChild(parent, plan.ObjectName);

            if (plan.TestOnly)
                return RunTransientCreateValidation(plan, scene);

            var created = new GameObject(plan.ObjectName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(created, $"Create UI object '{plan.ObjectName}' via Unity MCP");
            created.transform.SetParent(parent, false);
            ConfigureDefaultRectTransform((RectTransform)created.transform);
            EditorSceneManager.MarkSceneDirty(scene);

            var createdInstanceId = created.GetInstanceID();
            var createdGlobalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(created).ToString();
            var createdPath = GetHierarchyPath(created.transform);
            var saved = false;
            if (plan.SaveScene)
                saved = EditorSceneManager.SaveScene(scene);

            return new CreateUiObjectApplyResult
            {
                operation = "create_ui_object",
                applied = true,
                tokenConsumed = true,
                testOnly = false,
                scopeKind = "loaded_scene",
                scopePath = plan.ScenePath,
                parentPath = plan.ParentPath,
                objectName = plan.ObjectName,
                createdHierarchyPath = createdPath,
                createdInstanceId = createdInstanceId,
                createdGlobalObjectId = createdGlobalObjectId,
                rectTransform = DefaultRectTransform(),
                undoRegistered = true,
                destroyedAfterValidation = false,
                saved = saved,
                sceneDirty = scene.isDirty,
                warning = plan.SaveScene && !saved
                    ? "The object was created, but Unity did not save the scene; the scene remains dirty."
                    : string.Empty
            };
        }

        private static CreateUiObjectApplyResult RunTransientCreateValidation(CreateUiObjectPlan plan, Scene scene)
        {
            var sceneDirtyBefore = scene.isDirty;
            var transient = new GameObject(plan.ObjectName, typeof(RectTransform))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var instanceId = transient.GetInstanceID();
            try
            {
                ConfigureDefaultRectTransform((RectTransform)transient.transform);
                return new CreateUiObjectApplyResult
                {
                    operation = "create_ui_object",
                    applied = true,
                    tokenConsumed = true,
                    testOnly = true,
                    scopeKind = "loaded_scene",
                    scopePath = plan.ScenePath,
                    parentPath = plan.ParentPath,
                    objectName = plan.ObjectName,
                    createdHierarchyPath = string.Empty,
                    createdInstanceId = instanceId,
                    createdGlobalObjectId = string.Empty,
                    rectTransform = DefaultRectTransform(),
                    undoRegistered = false,
                    destroyedAfterValidation = true,
                    saved = false,
                    sceneDirty = scene.isDirty,
                    warning = scene.isDirty != sceneDirtyBefore
                        ? "Unexpected scene dirty-state change during transient validation."
                        : string.Empty
                };
            }
            finally
            {
                Object.DestroyImmediate(transient);
            }
        }

        private static void EnsureEditorAllowsMutation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
                throw new InvalidOperationException("Mutation planning/apply is disabled while playing or changing play mode.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Mutation planning/apply is disabled while Unity is compiling or updating.");
        }

        private static Scene ResolveLoadedSceneForMutation(string scenePath)
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!string.Equals(scene.path, scenePath, StringComparison.Ordinal))
                    continue;
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                    throw new InvalidOperationException($"Scene '{scenePath}' is not a normal loaded scene.");
                return scene;
            }

            throw new InvalidOperationException($"Scene '{scenePath}' is not loaded. This mutation tool never opens scenes.");
        }

        private static string ValidateHierarchyPath(string path)
        {
            var value = (path ?? string.Empty).Trim();
            if (value.Length == 0 || value.Length > 512 || value.StartsWith("/", StringComparison.Ordinal) ||
                value.EndsWith("/", StringComparison.Ordinal) || value.Contains("//", StringComparison.Ordinal) ||
                Regex.IsMatch(value, "[\\x00-\\x1f]"))
            {
                throw new ArgumentException("parent_path must be an explicit slash-separated hierarchy path of at most 512 characters.");
            }
            return value;
        }

        private static string ValidateUiObjectName(string name)
        {
            var value = name ?? string.Empty;
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                !Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9 _-]{0,63}$"))
            {
                throw new ArgumentException("object_name must start with a letter and contain at most 64 letters, digits, spaces, underscores, or hyphens.");
            }
            return value;
        }

        private static Transform ResolveHierarchyPath(Scene scene, string hierarchyPath)
        {
            var segments = hierarchyPath.Split('/');
            Transform current = null;
            var rootMatches = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!string.Equals(root.name, segments[0], StringComparison.Ordinal))
                    continue;
                current = root.transform;
                rootMatches++;
            }
            if (rootMatches != 1)
                throw new InvalidOperationException($"parent_path root '{segments[0]}' matched {rootMatches} scene objects; exactly one is required.");

            for (var segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
            {
                Transform next = null;
                var matches = 0;
                for (var childIndex = 0; childIndex < current.childCount; childIndex++)
                {
                    var child = current.GetChild(childIndex);
                    if (!string.Equals(child.name, segments[segmentIndex], StringComparison.Ordinal))
                        continue;
                    next = child;
                    matches++;
                }
                if (matches != 1)
                    throw new InvalidOperationException($"parent_path segment '{segments[segmentIndex]}' matched {matches} children; exactly one is required.");
                current = next;
            }

            return current;
        }

        private static void RejectExistingChild(Transform parent, string objectName)
        {
            for (var index = 0; index < parent.childCount; index++)
                if (string.Equals(parent.GetChild(index).name, objectName, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Parent '{GetHierarchyPath(parent)}' already has a direct child named '{objectName}'. Nothing was changed.");
        }

        private static string GetDirectChildSignature(Transform parent)
        {
            var builder = new StringBuilder();
            builder.Append(parent.childCount).Append('|');
            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                builder.Append(child.GetInstanceID()).Append(':').Append(child.name).Append('|');
            }
            return builder.ToString();
        }

        private static void ConfigureDefaultRectTransform(RectTransform value)
        {
            value.anchorMin = new Vector2(0.5f, 0.5f);
            value.anchorMax = new Vector2(0.5f, 0.5f);
            value.pivot = new Vector2(0.5f, 0.5f);
            value.anchoredPosition = Vector2.zero;
            value.sizeDelta = new Vector2(100f, 100f);
            value.localRotation = Quaternion.identity;
            value.localScale = Vector3.one;
        }

        private static RectTransformDefaults DefaultRectTransform()
        {
            return new RectTransformDefaults
            {
                anchorMin = "0.5,0.5",
                anchorMax = "0.5,0.5",
                pivot = "0.5,0.5",
                anchoredPosition = "0,0",
                sizeDelta = "100,100",
                localScale = "1,1,1"
            };
        }

        private static string CreatePlanToken()
        {
            var bytes = new byte[24];
            using (var generator = RandomNumberGenerator.Create())
                generator.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static void PruneMutationPlans()
        {
            var now = DateTime.UtcNow;
            var expired = new List<string>();
            foreach (var pair in CreateUiObjectPlans)
                if (pair.Value.ExpiresAtUtc <= now)
                    expired.Add(pair.Key);
            foreach (var token in expired)
                CreateUiObjectPlans.Remove(token);
        }

        private static void RemoveOldestMutationPlan()
        {
            string oldestToken = null;
            var oldestTime = DateTime.MaxValue;
            foreach (var pair in CreateUiObjectPlans)
            {
                if (pair.Value.CreatedAtUtc >= oldestTime)
                    continue;
                oldestTime = pair.Value.CreatedAtUtc;
                oldestToken = pair.Key;
            }
            if (oldestToken != null)
                CreateUiObjectPlans.Remove(oldestToken);
        }

        private static EditorStatusResult GetEditorStatus()
        {
            var scene = SceneManager.GetActiveScene();
            return new EditorStatusResult
            {
                projectPath = ProjectRoot,
                unityVersion = Application.unityVersion,
                productName = Application.productName,
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                activeScene = ToSceneSummary(scene),
                loadedSceneCount = SceneManager.sceneCount,
                selectionCount = Selection.objects.Length,
                bridgeMode = "confirmation-gated"
            };
        }

        private static SelectionResult GetSelection()
        {
            var selected = Selection.objects;
            var items = new SelectionItem[selected.Length];
            for (var index = 0; index < selected.Length; index++)
            {
                var value = selected[index];
                var gameObject = AsGameObject(value);
                items[index] = new SelectionItem
                {
                    name = value != null ? value.name : "<missing>",
                    type = value != null ? value.GetType().FullName : "<missing>",
                    instanceId = value != null ? value.GetInstanceID() : 0,
                    globalObjectId = value != null ? GlobalObjectId.GetGlobalObjectIdSlow(value).ToString() : string.Empty,
                    assetPath = value != null ? AssetDatabase.GetAssetPath(value) : string.Empty,
                    hierarchyPath = gameObject != null ? GetHierarchyPath(gameObject.transform) : string.Empty,
                    scenePath = gameObject != null ? gameObject.scene.path : string.Empty,
                    isPersistent = value != null && EditorUtility.IsPersistent(value)
                };
            }

            return new SelectionResult
            {
                activeObjectInstanceId = Selection.activeObject != null ? Selection.activeObject.GetInstanceID() : 0,
                count = items.Length,
                items = items
            };
        }

        private static AssetSearchResult FindAssets(FindAssetsArguments arguments)
        {
            arguments ??= new FindAssetsArguments();
            var query = (arguments.query ?? string.Empty).Trim();
            if (query.Length > 200)
                throw new ArgumentException("query must be at most 200 characters.");

            var type = (arguments.type ?? string.Empty).Trim();
            if (type.Length > 0 && !Regex.IsMatch(type, "^[A-Za-z0-9_.]+$"))
                throw new ArgumentException("type may contain only letters, digits, underscores, and dots.");

            var folders = NormalizeFolders(arguments.folders);
            var limit = Clamp(arguments.limit, 1, 500, 100);
            var filter = type.Length == 0 ? query : $"{query} t:{type}".Trim();
            var guids = folders.Length == 0
                ? AssetDatabase.FindAssets(filter)
                : AssetDatabase.FindAssets(filter, folders);

            var paths = new string[guids.Length];
            for (var index = 0; index < guids.Length; index++)
                paths[index] = AssetDatabase.GUIDToAssetPath(guids[index]);
            Array.Sort(paths, StringComparer.Ordinal);

            var count = Math.Min(paths.Length, limit);
            var items = new AssetSearchItem[count];
            for (var index = 0; index < count; index++)
            {
                var path = paths[index];
                var mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                items[index] = new AssetSearchItem
                {
                    guid = AssetDatabase.AssetPathToGUID(path),
                    path = path,
                    name = Path.GetFileNameWithoutExtension(path),
                    type = mainType != null ? mainType.FullName : string.Empty
                };
            }

            return new AssetSearchResult
            {
                query = query,
                type = type,
                folders = folders,
                totalMatches = paths.Length,
                returned = items.Length,
                truncated = paths.Length > items.Length,
                assets = items
            };
        }

        private static SceneHierarchyResult GetSceneHierarchy(SceneHierarchyArguments arguments)
        {
            arguments ??= new SceneHierarchyArguments();
            var scene = ResolveLoadedScene(arguments.scenePath);
            var maxDepth = Clamp(arguments.maxDepth, 0, 20, 8);
            var maxNodes = Clamp(arguments.maxNodes, 1, 1000, 250);
            var nodes = new List<HierarchyNode>(Math.Min(maxNodes, 256));
            var truncated = false;

            foreach (var root in scene.GetRootGameObjects())
            {
                AppendHierarchy(root.transform, 0, maxDepth, maxNodes, arguments.includeInactive,
                    arguments.includeComponents, nodes, ref truncated);
                if (nodes.Count >= maxNodes)
                    break;
            }

            return new SceneHierarchyResult
            {
                scene = ToSceneSummary(scene),
                maxDepth = maxDepth,
                maxNodes = maxNodes,
                nodeCount = nodes.Count,
                truncated = truncated,
                nodes = nodes.ToArray()
            };
        }

        private static PrefabInspectionResult InspectPrefab(InspectPrefabArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.assetPath))
                throw new ArgumentException("asset_path is required.");

            var assetPath = NormalizeAssetPath(arguments.assetPath);
            if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("asset_path must reference a .prefab asset.");

            var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (root == null)
                throw new FileNotFoundException($"Prefab was not found at '{assetPath}'.");

            var maxDepth = Clamp(arguments.maxDepth, 0, 20, 10);
            var maxNodes = Clamp(arguments.maxNodes, 1, 1000, 300);
            var nodes = new List<HierarchyNode>(Math.Min(maxNodes, 256));
            var truncated = false;
            AppendHierarchy(root.transform, 0, maxDepth, maxNodes, true, true, nodes, ref truncated);

            return new PrefabInspectionResult
            {
                assetPath = assetPath,
                guid = AssetDatabase.AssetPathToGUID(assetPath),
                rootName = root.name,
                prefabAssetType = PrefabUtility.GetPrefabAssetType(root).ToString(),
                maxDepth = maxDepth,
                maxNodes = maxNodes,
                nodeCount = nodes.Count,
                truncated = truncated,
                nodes = nodes.ToArray()
            };
        }

        private static void AppendHierarchy(
            Transform transform,
            int depth,
            int maxDepth,
            int maxNodes,
            bool includeInactive,
            bool includeComponents,
            List<HierarchyNode> nodes,
            ref bool truncated)
        {
            if (nodes.Count >= maxNodes)
            {
                truncated = true;
                return;
            }

            var gameObject = transform.gameObject;
            if (!includeInactive && !gameObject.activeInHierarchy)
                return;

            nodes.Add(new HierarchyNode
            {
                path = GetHierarchyPath(transform),
                name = gameObject.name,
                depth = depth,
                siblingIndex = transform.GetSiblingIndex(),
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                layer = LayerMask.LayerToName(gameObject.layer),
                tag = gameObject.tag,
                components = includeComponents ? GetComponents(gameObject) : Array.Empty<ComponentSummary>()
            });

            if (depth >= maxDepth)
            {
                if (transform.childCount > 0)
                    truncated = true;
                return;
            }

            for (var index = 0; index < transform.childCount; index++)
            {
                AppendHierarchy(transform.GetChild(index), depth + 1, maxDepth, maxNodes, includeInactive,
                    includeComponents, nodes, ref truncated);
                if (nodes.Count >= maxNodes)
                {
                    if (index + 1 < transform.childCount)
                        truncated = true;
                    return;
                }
            }
        }

        private static ComponentSummary[] GetComponents(GameObject gameObject)
        {
            var components = gameObject.GetComponents<Component>();
            var results = new ComponentSummary[components.Length];
            for (var index = 0; index < components.Length; index++)
            {
                var component = components[index];
                if (component == null)
                {
                    results[index] = new ComponentSummary { type = "<missing-script>" };
                    continue;
                }

                var scriptPath = string.Empty;
                if (component is MonoBehaviour behaviour)
                {
                    var script = MonoScript.FromMonoBehaviour(behaviour);
                    if (script != null)
                        scriptPath = AssetDatabase.GetAssetPath(script);
                }

                results[index] = new ComponentSummary
                {
                    type = component.GetType().FullName,
                    scriptAssetPath = scriptPath,
                    enabled = component is Behaviour enabledBehaviour ? enabledBehaviour.enabled : true
                };
            }

            return results;
        }

        private static Scene ResolveLoadedScene(string requestedPath)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
                return SceneManager.GetActiveScene();

            var normalized = requestedPath.Replace('\\', '/').Trim();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (string.Equals(scene.path, normalized, StringComparison.Ordinal) ||
                    string.Equals(scene.name, normalized, StringComparison.Ordinal))
                    return scene;
            }

            throw new ArgumentException($"Scene '{requestedPath}' is not currently loaded. This bridge never opens scenes.");
        }

        private static SceneSummary ToSceneSummary(Scene scene)
        {
            return new SceneSummary
            {
                name = scene.name,
                path = scene.path,
                buildIndex = scene.buildIndex,
                isLoaded = scene.isLoaded,
                isDirty = scene.IsValid() && scene.isLoaded && EditorSceneManager.IsPreviewScene(scene) == false && scene.isDirty,
                rootCount = scene.IsValid() && scene.isLoaded ? scene.rootCount : 0
            };
        }

        private static GameObject AsGameObject(Object value)
        {
            return value switch
            {
                GameObject gameObject => gameObject,
                Component component => component.gameObject,
                _ => null
            };
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Push(current.name);
            return string.Join("/", names);
        }

        private static string[] NormalizeFolders(string[] folders)
        {
            if (folders == null || folders.Length == 0)
                return Array.Empty<string>();
            if (folders.Length > 20)
                throw new ArgumentException("At most 20 folders may be searched.");

            var normalized = new string[folders.Length];
            for (var index = 0; index < folders.Length; index++)
            {
                normalized[index] = NormalizeAssetPath(folders[index]);
                if (!AssetDatabase.IsValidFolder(normalized[index]))
                    throw new ArgumentException($"'{normalized[index]}' is not a valid project folder.");
            }

            return normalized;
        }

        private static string NormalizeAssetPath(string path)
        {
            var normalized = (path ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
            if (normalized.Length == 0 ||
                !(normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal)) ||
                normalized.Contains("../", StringComparison.Ordinal) ||
                normalized.EndsWith("/..", StringComparison.Ordinal))
            {
                throw new ArgumentException("Paths must stay within the project's Assets folder.");
            }

            return normalized;
        }

        private static int Clamp(int value, int minimum, int maximum, int fallback)
        {
            if (value == 0)
                return fallback;
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static int ResolvePort()
        {
            var raw = Environment.GetEnvironmentVariable("GUILDIDLE_UNITY_MCP_PORT");
            return int.TryParse(raw, out var value) && value >= 1024 && value <= 65535
                ? value
                : DefaultPort;
        }

        private static string CreateToken()
        {
            var bytes = new byte[32];
            using (var generator = RandomNumberGenerator.Create())
                generator.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static void WriteDescriptor()
        {
            var directory = Path.GetDirectoryName(_descriptorPath);
            Directory.CreateDirectory(directory ?? throw new InvalidOperationException("Invalid descriptor path."));
            var descriptor = new BridgeDescriptor
            {
                protocolVersion = 2,
                projectPath = ProjectRoot,
                unityVersion = Application.unityVersion,
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                host = "127.0.0.1",
                port = _port,
                token = _token,
                mode = "confirmation-gated"
            };
            var temporaryPath = _descriptorPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(descriptor, true), new UTF8Encoding(false));
            File.Copy(temporaryPath, _descriptorPath, true);
            File.Delete(temporaryPath);
        }

        private static void TryDeleteDescriptor()
        {
            try
            {
                if (!string.IsNullOrEmpty(_descriptorPath) && File.Exists(_descriptorPath))
                    File.Delete(_descriptorPath);
            }
            catch (IOException)
            {
                // A stale descriptor is harmless: the adapter also verifies connectivity.
            }
        }

        private static HttpRequest ReadRequest(NetworkStream stream)
        {
            var headerBytes = new List<byte>(512);
            var matched = 0;
            var delimiter = new byte[] { 13, 10, 13, 10 };
            while (headerBytes.Count < MaxHeaderBytes)
            {
                var value = stream.ReadByte();
                if (value < 0)
                    throw new IOException("Connection closed before HTTP headers completed.");
                headerBytes.Add((byte)value);
                matched = value == delimiter[matched] ? matched + 1 : value == delimiter[0] ? 1 : 0;
                if (matched == delimiter.Length)
                    break;
            }

            if (matched != delimiter.Length)
                throw new InvalidDataException("HTTP headers are too large.");

            var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
                throw new InvalidDataException("Invalid HTTP request line.");

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < lines.Length; index++)
            {
                var separator = lines[index].IndexOf(':');
                if (separator <= 0)
                    continue;
                headers[lines[index].Substring(0, separator).Trim()] = lines[index].Substring(separator + 1).Trim();
            }

            var contentLength = 0;
            if (headers.TryGetValue("Content-Length", out var lengthText) &&
                (!int.TryParse(lengthText, out contentLength) || contentLength < 0 || contentLength > MaxRequestBytes))
            {
                throw new InvalidDataException($"Content-Length must be between 0 and {MaxRequestBytes} bytes.");
            }

            var bodyBytes = new byte[contentLength];
            var offset = 0;
            while (offset < bodyBytes.Length)
            {
                var read = stream.Read(bodyBytes, offset, bodyBytes.Length - offset);
                if (read <= 0)
                    throw new IOException("Connection closed before the HTTP body completed.");
                offset += read;
            }

            return new HttpRequest
            {
                Method = requestLine[0].ToUpperInvariant(),
                Path = requestLine[1],
                Headers = headers,
                Body = Encoding.UTF8.GetString(bodyBytes)
            };
        }

        private static void WriteResponse(NetworkStream stream, int statusCode, string statusText, string body)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n");
            stream.Write(headers, 0, headers.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
        }

        private static string SuccessResponse(string resultJson)
        {
            return $"{{\"ok\":true,\"resultJson\":\"{EscapeJson(resultJson)}\"}}";
        }

        private static string ErrorResponse(string message)
        {
            return $"{{\"ok\":false,\"error\":\"{EscapeJson(message)}\"}}";
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length + 16);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 32)
                            builder.Append($"\\u{(int)character:x4}");
                        else
                            builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Could not resolve Unity project root.");

        private sealed class PendingRequest
        {
            public PendingRequest(string requestJson)
            {
                RequestJson = requestJson;
            }

            public string RequestJson { get; }
            public string ResponseJson { get; set; }
            public ManualResetEventSlim Completed { get; } = new ManualResetEventSlim(false);
        }

        private sealed class HttpRequest
        {
            public string Method;
            public string Path;
            public Dictionary<string, string> Headers;
            public string Body;
        }

        [Serializable]
        private sealed class BridgeRequest
        {
            public string tool;
            public string argumentsJson;
        }

        [Serializable]
        private sealed class BridgeDescriptor
        {
            public int protocolVersion;
            public string projectPath;
            public string unityVersion;
            public int processId;
            public string host;
            public int port;
            public string token;
            public string mode;
        }

        [Serializable]
        private sealed class FindAssetsArguments
        {
            public string query;
            public string type;
            public string[] folders;
            public int limit;
        }

        [Serializable]
        private sealed class SceneHierarchyArguments
        {
            public string scenePath;
            public int maxDepth;
            public int maxNodes;
            public bool includeInactive;
            public bool includeComponents;
        }

        [Serializable]
        private sealed class InspectPrefabArguments
        {
            public string assetPath;
            public int maxDepth;
            public int maxNodes;
        }

        [Serializable]
        private sealed class PlanCreateUiObjectArguments
        {
            public string scenePath;
            public string parentPath;
            public string objectName;
            public bool saveScene;
            public bool testOnly;
        }

        [Serializable]
        private sealed class ApplyCreateUiObjectArguments
        {
            public string planToken;
            public string confirmation;
        }

        private sealed class CreateUiObjectPlan
        {
            public string Token;
            public DateTime CreatedAtUtc;
            public DateTime ExpiresAtUtc;
            public string ScenePath;
            public string ParentPath;
            public string ParentGlobalObjectId;
            public string ParentChildSignature;
            public string ObjectName;
            public bool SaveScene;
            public bool TestOnly;
            public bool SceneWasDirty;
        }

        [Serializable]
        private sealed class CreateUiObjectPlanResult
        {
            public string operation;
            public string planToken;
            public string expiresAtUtc;
            public int expiresInSeconds;
            public string scopeKind;
            public string scopePath;
            public string parentPath;
            public string parentGlobalObjectId;
            public string objectName;
            public RectTransformDefaults rectTransform;
            public bool saveScene;
            public bool testOnly;
            public bool sceneDirtyAtPlan;
            public string plannedChange;
            public string confirmationRequired;
        }

        [Serializable]
        private sealed class CreateUiObjectApplyResult
        {
            public string operation;
            public bool applied;
            public bool tokenConsumed;
            public bool testOnly;
            public string scopeKind;
            public string scopePath;
            public string parentPath;
            public string objectName;
            public string createdHierarchyPath;
            public int createdInstanceId;
            public string createdGlobalObjectId;
            public RectTransformDefaults rectTransform;
            public bool undoRegistered;
            public bool destroyedAfterValidation;
            public bool saved;
            public bool sceneDirty;
            public string warning;
        }

        [Serializable]
        private sealed class RectTransformDefaults
        {
            public string anchorMin;
            public string anchorMax;
            public string pivot;
            public string anchoredPosition;
            public string sizeDelta;
            public string localScale;
        }

        [Serializable]
        private sealed class EditorStatusResult
        {
            public string projectPath;
            public string unityVersion;
            public string productName;
            public string activeBuildTarget;
            public bool isPlaying;
            public bool isPaused;
            public bool isCompiling;
            public bool isUpdating;
            public bool isPlayingOrWillChangePlaymode;
            public SceneSummary activeScene;
            public int loadedSceneCount;
            public int selectionCount;
            public string bridgeMode;
        }

        [Serializable]
        private sealed class SceneSummary
        {
            public string name;
            public string path;
            public int buildIndex;
            public bool isLoaded;
            public bool isDirty;
            public int rootCount;
        }

        [Serializable]
        private sealed class SelectionResult
        {
            public int activeObjectInstanceId;
            public int count;
            public SelectionItem[] items;
        }

        [Serializable]
        private sealed class SelectionItem
        {
            public string name;
            public string type;
            public int instanceId;
            public string globalObjectId;
            public string assetPath;
            public string hierarchyPath;
            public string scenePath;
            public bool isPersistent;
        }

        [Serializable]
        private sealed class AssetSearchResult
        {
            public string query;
            public string type;
            public string[] folders;
            public int totalMatches;
            public int returned;
            public bool truncated;
            public AssetSearchItem[] assets;
        }

        [Serializable]
        private sealed class AssetSearchItem
        {
            public string guid;
            public string path;
            public string name;
            public string type;
        }

        [Serializable]
        private sealed class SceneHierarchyResult
        {
            public SceneSummary scene;
            public int maxDepth;
            public int maxNodes;
            public int nodeCount;
            public bool truncated;
            public HierarchyNode[] nodes;
        }

        [Serializable]
        private sealed class PrefabInspectionResult
        {
            public string assetPath;
            public string guid;
            public string rootName;
            public string prefabAssetType;
            public int maxDepth;
            public int maxNodes;
            public int nodeCount;
            public bool truncated;
            public HierarchyNode[] nodes;
        }

        [Serializable]
        private sealed class HierarchyNode
        {
            public string path;
            public string name;
            public int depth;
            public int siblingIndex;
            public bool activeSelf;
            public bool activeInHierarchy;
            public string layer;
            public string tag;
            public ComponentSummary[] components;
        }

        [Serializable]
        private sealed class ComponentSummary
        {
            public string type;
            public string scriptAssetPath;
            public bool enabled;
        }
    }
}
