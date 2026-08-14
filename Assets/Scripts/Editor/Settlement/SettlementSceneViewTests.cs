using System;
using System.Collections.Generic;
using System.Reflection;
using GuildIdle.Configs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GuildIdle.Settlement.Editor
{
    public sealed class SettlementSceneViewTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null)
                    UnityEngine.Object.DestroyImmediate(_created[index]);

            _created.Clear();
        }

        [Test]
        public void AppliesStageMembershipAndTreatsLevelZeroAsRealState()
        {
            var root = CreateObject("SettlementMap");
            var hall = CreateBuilding(root.transform, "building_hall", 0, 1);
            var warehouse = CreateBuilding(root.transform, "building_warehouse", 0);
            var inactive = CreateBuilding(root.transform, "building_underwood", 0);
            var source = new TestRuntimeSource("stage_arrival")
                .AddMembership("stage_arrival", "building_hall", "building_warehouse")
                .SetLevel("building_hall", 1)
                .SetLevel("building_warehouse", 0);
            var sceneView = CreateSceneView(root.transform, source, "stage_arrival");

            sceneView.RefreshNow();

            Assert.That(hall.gameObject.activeSelf, Is.True);
            Assert.That(hall.CurrentLevel, Is.EqualTo(1));
            Assert.That(warehouse.gameObject.activeSelf, Is.True);
            Assert.That(warehouse.CurrentLevel, Is.EqualTo(0));
            Assert.That(inactive.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void MissingPlayerStateDoesNotFallBackToLevelZero()
        {
            var root = CreateObject("SettlementMap");
            var hall = CreateBuilding(root.transform, "building_hall", 0);
            var source = new TestRuntimeSource("stage_arrival")
                .AddMembership("stage_arrival", "building_hall");
            LogAssert.Expect(LogType.Error, "[SettlementSceneView] Active building 'building_hall' for stage 'stage_arrival' has no PlayerState building state.");

            CreateSceneView(root.transform, source, "stage_arrival").RefreshNow();

            Assert.That(hall.CurrentLevel, Is.Null);
        }

        [Test]
        public void MissingLevelVisualIsReportedOnceUntilLevelChangesOrSucceeds()
        {
            var root = CreateObject("SettlementMap");
            var hall = CreateBuilding(root.transform, "building_hall");
            var source = new TestRuntimeSource("stage_arrival")
                .AddMembership("stage_arrival", "building_hall")
                .SetLevel("building_hall", 1);
            LogAssert.Expect(LogType.Error, "[BuildingView] Building 'building_hall' has no visual for level 1.");
            var sceneView = CreateSceneView(root.transform, source, "stage_arrival");

            sceneView.RefreshNow();
            sceneView.RefreshNow();
            sceneView.RefreshNow();

            LogAssert.Expect(LogType.Error, "[BuildingView] Building 'building_hall' has no visual for level 2.");
            source.SetLevel("building_hall", 2);
            sceneView.RefreshNow();
            sceneView.RefreshNow();

            SetLevelVisuals(hall, 2);
            sceneView.RefreshNow();
            Assert.That(hall.CurrentLevel, Is.EqualTo(2));

            hall.ClearVisuals();
            SetLevelVisuals(hall, 3);
            source.SetLevel("building_hall", 3);
            sceneView.RefreshNow();
            Assert.That(hall.CurrentLevel, Is.EqualTo(3));

            LogAssert.Expect(LogType.Error, "[BuildingView] Building 'building_hall' has no visual for level 2.");
            source.SetLevel("building_hall", 2);
            sceneView.RefreshNow();
        }

        [Test]
        public void StageChangeReusesMapAndAppliesNewMembershipAndCameraPreset()
        {
            var root = CreateObject("SettlementMap");
            var mapIdentity = root.GetInstanceID();
            var hall = CreateBuilding(root.transform, "building_hall", 0);
            var underwood = CreateBuilding(root.transform, "building_underwood", 0);
            var source = new TestRuntimeSource("stage_arrival")
                .AddMembership("stage_arrival", "building_hall")
                .AddMembership("stage_2", "building_underwood")
                .SetLevel("building_hall", 0)
                .SetLevel("building_underwood", 0);
            var sceneView = CreateSceneView(root.transform, source, "stage_arrival", "stage_2");

            Assert.That(hall.gameObject.activeSelf, Is.True);
            Assert.That(underwood.gameObject.activeSelf, Is.False);

            source.CurrentStageId = "stage_2";
            sceneView.RefreshNow();

            Assert.That(root.GetInstanceID(), Is.EqualTo(mapIdentity));
            Assert.That(sceneView.AppliedStageId, Is.EqualTo("stage_2"));
            Assert.That(hall.gameObject.activeSelf, Is.False);
            Assert.That(underwood.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void LookupReportsEmptyAndDuplicateBuildingIds()
        {
            var root = CreateObject("SettlementMap");
            CreateBuilding(root.transform, string.Empty, 0);
            CreateBuilding(root.transform, "building_hall", 0);
            CreateBuilding(root.transform, "building_hall", 0);
            var source = new TestRuntimeSource("stage_arrival")
                .AddMembership("stage_arrival", "building_hall")
                .SetLevel("building_hall", 0);
            LogAssert.Expect(LogType.Error, "[SettlementSceneView] BuildingView on 'Building_empty' has empty building_id.");
            LogAssert.Expect(LogType.Error, "[SettlementSceneView] Duplicate BuildingView building_id 'building_hall'.");

            CreateSceneView(root.transform, source, "stage_arrival").RefreshNow();
        }

        private SettlementSceneView CreateSceneView(
            Transform buildingRoot,
            ISettlementSceneRuntimeSource source,
            params string[] cameraStageIds)
        {
            var host = CreateObject("SettlementSceneView");
            host.SetActive(false);
            var cameraObject = CreateObject("SettlementCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var catalog = ScriptableObject.CreateInstance<SettlementStageViewCatalog>();
            _created.Add(catalog);
            SetField(catalog, "cameraPresets", CreateCameraPresets(cameraStageIds));

            var sceneView = host.AddComponent<SettlementSceneView>();
            SetField(sceneView, "buildingRoot", buildingRoot);
            SetField(sceneView, "sceneCamera", camera);
            SetField(sceneView, "stageViewCatalog", catalog);
            sceneView.SetRuntimeSource(source);
            return sceneView;
        }

        private BuildingView CreateBuilding(Transform parent, string buildingId, params int[] levels)
        {
            var building = CreateObject(string.IsNullOrEmpty(buildingId) ? "Building_empty" : buildingId);
            building.transform.SetParent(parent, false);
            var view = building.AddComponent<BuildingView>();
            SetField(view, "buildingId", buildingId);
            SetLevelVisuals(view, levels);
            return view;
        }

        private void SetLevelVisuals(BuildingView view, params int[] levels)
        {
            var entries = new BuildingView.LevelVisual[levels.Length];
            for (var index = 0; index < levels.Length; index++)
            {
                var visual = CreateObject($"Level_{levels[index]}");
                visual.transform.SetParent(view.transform, false);
                var entry = new BuildingView.LevelVisual();
                SetField(entry, "level", levels[index]);
                SetField(entry, "visual", visual);
                entries[index] = entry;
            }

            SetField(view, "levelVisuals", entries);
        }

        private static SettlementStageViewCatalog.CameraPreset[] CreateCameraPresets(string[] stageIds)
        {
            var presets = new SettlementStageViewCatalog.CameraPreset[stageIds.Length];
            for (var index = 0; index < stageIds.Length; index++)
            {
                var preset = new SettlementStageViewCatalog.CameraPreset();
                SetField(preset, "stageId", stageIds[index]);
                SetField(preset, "position", new Vector3(index, 10f, index));
                SetField(preset, "eulerAngles", new Vector3(45f, 0f, 0f));
                SetField(preset, "fieldOfView", 34f);
                presets[index] = preset;
            }

            return presets;
        }

        private GameObject CreateObject(string name)
        {
            var value = new GameObject(name);
            _created.Add(value);
            return value;
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }

        private sealed class TestRuntimeSource : ISettlementSceneRuntimeSource
        {
            private readonly Dictionary<string, SettlementStageBuildingConfigDto[]> _memberships =
                new Dictionary<string, SettlementStageBuildingConfigDto[]>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _levels = new Dictionary<string, int>(StringComparer.Ordinal);

            public TestRuntimeSource(string stageId)
            {
                CurrentStageId = stageId;
            }

            public bool IsReady => true;
            public string CurrentStageId { get; set; }

            public TestRuntimeSource AddMembership(string stageId, params string[] buildingIds)
            {
                var rows = new SettlementStageBuildingConfigDto[buildingIds.Length];
                for (var index = 0; index < buildingIds.Length; index++)
                {
                    rows[index] = new SettlementStageBuildingConfigDto
                    {
                        stageId = stageId,
                        buildingId = buildingIds[index],
                        enabled = true
                    };
                }

                _memberships[stageId] = rows;
                return this;
            }

            public TestRuntimeSource SetLevel(string buildingId, int level)
            {
                _levels[buildingId] = level;
                return this;
            }

            public SettlementStageBuildingConfigDto[] GetStageBuildings(string stageId)
            {
                return _memberships.TryGetValue(stageId, out var rows)
                    ? rows
                    : Array.Empty<SettlementStageBuildingConfigDto>();
            }

            public bool TryGetBuildingLevel(string buildingId, out int level)
            {
                return _levels.TryGetValue(buildingId, out level);
            }
        }
    }
}
