using System;
using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Player
{
    public sealed class PlayerStateTests
    {
        private PlayerStateFactory _factory;

        [SetUp]
        public void SetUp()
        {
            var database = CreateDatabase();
            RuntimeConfigs.SetDatabaseForTests(database);
            _factory = TestPlayerComposition.CreatePlayerStateFactory(database);
        }

        [Test]
        public void CreateDefault_AppliesStarterHeroAndEquipment()
        {
            var state = _factory.CreateDefault();

            Assert.That(state.IsHeroUnlocked("ren"), Is.True);
            Assert.That(state.HasHero("ren"), Is.True);
            Assert.That(state.HasHeroState("ren"), Is.True);
            Assert.That(state.GetHeroMaxFatigue("ren"), Is.EqualTo(121));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren")));
            Assert.That(state.GetItem("item_wooden_club"), Is.EqualTo(1));
            Assert.That(state.IsBuildingUnlocked("building_hall"), Is.True);
            Assert.That(state.GetBuildingLevel("building_hall"), Is.EqualTo(0));
            Assert.That(state.IsBuildingUnlocked("building_watchtower"), Is.True);
            Assert.That(state.GetBuildingLevel("building_watchtower"), Is.EqualTo(0));
            Assert.That(state.IsBuildingUnlocked("building_tavern"), Is.True);
            Assert.That(state.GetBuildingLevel("building_tavern"), Is.EqualTo(1));

            var saveData = state.ToSaveData();
            Assert.That(saveData.heroes, Has.Length.EqualTo(1));
            Assert.That(saveData.heroes[0].heroId, Is.EqualTo("ren"));
        }

        [Test]
        public void Items_AddAndSpendWithoutGoingNegative()
        {
            var state = _factory.CreateDefault();

            Assert.That(state.AddItem("resource_pine_wood", 3), Is.True);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(3));
            Assert.That(state.SpendItem("resource_pine_wood", 2), Is.True);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(1));
            Assert.That(state.SpendItem("resource_pine_wood", 2), Is.False);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(1));
        }

        [Test]
        public void Currency_IsSeparateFromItems()
        {
            var state = _factory.CreateDefault();

            Assert.That(state.AddCurrency("gold_id", 10), Is.True);
            Assert.That(state.GetCurrency("gold_id"), Is.EqualTo(10));
            Assert.That(state.SpendCurrency("gold_id", 4), Is.True);
            Assert.That(state.GetCurrency("gold_id"), Is.EqualTo(6));

            using (var logs = new CapturingLogHandler())
            {
                Assert.That(state.AddItem("gold_id", 1), Is.False);
                logs.AssertErrorContains("[PlayerState] Unknown item id 'gold_id'.");
            }
        }

        [Test]
        public void AddHero_DoesNotDuplicate()
        {
            var state = _factory.CreateDefault();

            Assert.That(state.AddHero("ren"), Is.False);

            var saveData = state.ToSaveData();
            Assert.That(saveData.acquiredHeroes, Is.EqualTo(new[] { "ren" }));
            Assert.That(saveData.unlockedHeroes, Is.EqualTo(new[] { "ren" }));
        }

        [Test]
        public void BuildingLevel_RequiresUnlockedBuilding()
        {
            var state = _factory.CreateDefault();

            using (var logs = new CapturingLogHandler())
            {
                Assert.That(state.SetBuildingLevel("building_hidden", 1), Is.False);
                logs.AssertErrorContains("[PlayerState] Cannot set level for locked building 'building_hidden'.");
            }

            Assert.That(state.GetBuildingLevel("building_hidden"), Is.EqualTo(0));

            Assert.That(state.UnlockBuilding("building_hidden"), Is.True);
            Assert.That(state.GetBuildingLevel("building_hidden"), Is.EqualTo(0));
            Assert.That(state.SetBuildingLevel("building_hidden", 0), Is.True);
            Assert.That(state.SetBuildingLevel("building_hidden", 1), Is.True);
        }

        [Test]
        public void BuildingClickability_UsesClickableRequirement()
        {
            var state = _factory.CreateDefault();

            Assert.That(state.CanClickBuilding("building_hall"), Is.True);
            Assert.That(state.CanClickBuilding("building_tavern"), Is.True);
            Assert.That(state.CanClickBuilding("building_watchtower"), Is.False);

            Assert.That(state.SetBuildingLevel("building_hall", 1), Is.True);

            Assert.That(state.CanClickBuilding("building_watchtower"), Is.True);
        }

        [Test]
        public void LocationAndActivity_CanBeUnlockedAndCompleted()
        {
            var state = _factory.CreateDefault();

            Assert.That(state.UnlockLocation("old_wolf_den_1_1"), Is.True);
            Assert.That(state.IsLocationUnlocked("old_wolf_den_1_1"), Is.True);
            Assert.That(state.CompleteActivity("combat_first_map_node"), Is.True);
            Assert.That(state.IsActivityCompleted("combat_first_map_node"), Is.True);
        }

        [Test]
        public void UnknownId_DoesNotMutateState()
        {
            var state = _factory.CreateDefault();
            var before = state.GetItem("resource_pine_wood");

            using (var logs = new CapturingLogHandler())
            {
                Assert.That(state.AddItem("missing_item", 1), Is.False);
                logs.AssertErrorContains("[PlayerState] Unknown item id 'missing_item'.");
            }

            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(before));
        }

        [Test]
        public void HeroState_SaveLoadRoundtripPreservesRuntimeState()
        {
            var state = _factory.CreateDefault();
            var maxFatigue = state.GetHeroMaxFatigue("ren");

            Assert.That(state.SpendHeroFatigue("ren", 5), Is.True);
            Assert.That(state.AddHeroSkillExp("ren", "skill_gathering", 150), Is.True);
            Assert.That(state.AddActivityExecution(new ActivityExecutionSaveData
            {
                executionId = "exec_1",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = GuildIdle.Core.ActivityRuntimeStatus.Running
            }), Is.True);

            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);
            var restored = SaveService.Load(_factory, storage);

            Assert.That(restored.GetHeroFatigue("ren"), Is.EqualTo(maxFatigue - 5));
            Assert.That(restored.GetHeroSkillExp("ren", "skill_gathering"), Is.EqualTo(150));
            Assert.That(restored.GetHeroSkillLevel("ren", "skill_gathering"), Is.EqualTo(2));
            Assert.That(restored.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo("exec_1"));
        }

        [Test]
        public void Load_V1SaveWithoutHeroStatesHydratesAcquiredHeroes()
        {
            var state = _factory.Create(
                new SaveData
                {
                    saveVersion = 1,
                    unlockedHeroes = new[] { "ren" },
                    acquiredHeroes = Array.Empty<string>()
                },
                new[] { new HeroSlotSaveEntry { slotIndex = 0, heroId = "ren" } });

            Assert.That(state.HasHeroState("ren"), Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren")));
            Assert.That(state.HasHero("ren"), Is.True);
            Assert.That(state.ToSaveData().saveVersion, Is.EqualTo(SaveData.CurrentSaveVersion));
        }

        [Test]
        public void SaveService_MigratesLegacyHeroSlotsWithoutWritingThemBack()
        {
            var storage = new MemorySaveStorage();
            storage.SetString(
                SaveService.SaveKey,
                "{\"saveVersion\":3,\"heroSlots\":[{\"slotIndex\":0,\"heroId\":\"ren\"}],\"activityRuntime\":{\"executions\":[{\"executionId\":\"exec_legacy\",\"activityId\":\"combat_first_map_node\",\"heroId\":\"ren\",\"heroSlotIndex\":0,\"status\":1}]}}");

            var state = SaveService.Load(_factory, storage);

            Assert.That(state.HasHero("ren"), Is.True);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
            Assert.That(state.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo("exec_legacy"));

            Assert.That(SaveService.Save(state, storage), Is.True);
            Assert.That(storage.GetString(SaveService.SaveKey, string.Empty), Does.Not.Contain("heroSlots"));
            Assert.That(storage.GetString(SaveService.SaveKey, string.Empty), Does.Not.Contain("heroSlotIndex"));
        }

        [Test]
        public void HeroBusy_IsIdempotentForSameExecutionAndRejectsDifferentExecution()
        {
            var state = _factory.CreateDefault();

            Assert.That(state.SetHeroBusy("ren", "exec_1"), Is.True);
            Assert.That(state.SetHeroBusy("ren", "exec_1"), Is.True);

            using (var logs = new CapturingLogHandler())
            {
                Assert.That(state.SetHeroBusy("ren", "exec_2"), Is.False);
                logs.AssertErrorContains("Hero 'ren' is already busy with execution 'exec_1'.");
                Assert.That(state.ClearHeroBusy("ren", "exec_2"), Is.False);
                logs.AssertErrorContains("Cannot clear busy state for hero 'ren' with execution 'exec_2'");
            }

            Assert.That(state.ClearHeroBusy("ren", "exec_1"), Is.True);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
        }

        [Test]
        public void SaveService_LoadsDefaultOnCorruptJson()
        {
            var storage = new MemorySaveStorage();
            storage.SetString(SaveService.SaveKey, "{ not valid json");

            PlayerState state;
            using (var logs = new CapturingLogHandler())
            {
                state = SaveService.Load(_factory, storage);
                logs.AssertErrorContains("[SaveService] Failed to load player save JSON. Creating default save.");
            }

            Assert.That(state.HasHero("ren"), Is.True);
            Assert.That(storage.HasKey(SaveService.SaveKey), Is.True);
            Assert.That(storage.GetString(SaveService.SaveKey, string.Empty), Does.Contain("\"heroId\":\"ren\""));
            Assert.That(storage.GetString(SaveService.SaveKey, string.Empty), Does.Not.Contain("heroSlots"));
        }

        [Test]
        public void SaveService_LoadCreatesDefaultPlayerPrefsJsonWhenMissing()
        {
            var storage = new MemorySaveStorage();

            var state = SaveService.Load(_factory, storage);

            Assert.That(state.HasHero("ren"), Is.True);
            Assert.That(storage.HasKey(SaveService.SaveKey), Is.True);
            Assert.That(storage.GetString(SaveService.SaveKey, string.Empty), Does.Contain("\"heroId\":\"ren\""));
        }

        [Test]
        public void SaveService_LoadsExistingV4ThroughProvidedFactoryWithoutChangingData()
        {
            var storage = new MemorySaveStorage();
            storage.SetString(
                SaveService.SaveKey,
                "{\"saveVersion\":4,\"currencies\":[{\"currencyId\":\"gold_id\",\"amount\":7}],\"items\":[{\"itemId\":\"resource_pine_wood\",\"amount\":3}],\"unlockedHeroes\":[\"ren\"],\"acquiredHeroes\":[\"ren\"],\"heroes\":[{\"heroId\":\"ren\",\"level\":1,\"exp\":0,\"fatigue\":77,\"maxFatigue\":121,\"skills\":[{\"skillId\":\"skill_gathering\",\"level\":2,\"exp\":150}]}],\"unlockedBuildings\":[\"building_tavern\"],\"buildingLevels\":[{\"buildingId\":\"building_tavern\",\"level\":1}]}");

            var state = SaveService.Load(_factory, storage);
            var saveData = state.ToSaveData();

            Assert.That(saveData.saveVersion, Is.EqualTo(4));
            Assert.That(state.GetCurrency("gold_id"), Is.EqualTo(7));
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(3));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(77));
            Assert.That(state.GetHeroMaxFatigue("ren"), Is.EqualTo(121));
            Assert.That(state.GetHeroSkillExp("ren", "skill_gathering"), Is.EqualTo(150));
            Assert.That(state.GetHeroSkillLevel("ren", "skill_gathering"), Is.EqualTo(2));
            Assert.That(state.IsBuildingUnlocked("building_tavern"), Is.True);
            Assert.That(state.GetBuildingLevel("building_tavern"), Is.EqualTo(1));
        }

        [Test]
        public void PlayerState_PublicConstructorsRequireHeroStatsService()
        {
            var constructors = typeof(PlayerState).GetConstructors();

            Assert.That(constructors, Has.Length.EqualTo(2));
            foreach (var constructor in constructors)
            {
                var hasHeroStats = false;
                foreach (var parameter in constructor.GetParameters())
                {
                    if (parameter.ParameterType == typeof(HeroStatsService))
                        hasHeroStats = true;
                }

                Assert.That(hasHeroStats, Is.True, constructor.ToString());
            }
        }

        [Test]
        public void SaveService_LoadAndResetOverloadsRequirePlayerStateFactory()
        {
            foreach (var method in typeof(SaveService).GetMethods())
            {
                if (method.Name != nameof(SaveService.Load) && method.Name != nameof(SaveService.ResetSave))
                    continue;

                var parameters = method.GetParameters();
                Assert.That(parameters, Is.Not.Empty, method.ToString());
                Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(PlayerStateFactory)), method.ToString());
            }
        }

        [Test]
        public void SaveService_ResetUsesProvidedFactoryBootstrap()
        {
            var storage = new MemorySaveStorage();
            var state = _factory.CreateDefault();
            Assert.That(state.AddCurrency("gold_id", 10), Is.True);
            Assert.That(SaveService.Save(state, storage), Is.True);

            var reset = SaveService.ResetSave(_factory, storage);

            Assert.That(reset.GetCurrency("gold_id"), Is.Zero);
            Assert.That(reset.HasHero("ren"), Is.True);
            Assert.That(reset.GetItem("item_wooden_club"), Is.EqualTo(1));
            Assert.That(reset.GetHeroMaxFatigue("ren"), Is.EqualTo(121));
        }

        private static ConfigDatabase CreateDatabase()
        {
            return new TestConfigDatabaseBuilder()
                .WithFullPlayerStateTestData()
                .Build();
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

            public bool HasKey(string key)
            {
                return _values.ContainsKey(key);
            }

            public string GetString(string key, string defaultValue)
            {
                return _values.TryGetValue(key, out var value) ? value : defaultValue;
            }

            public void SetString(string key, string value)
            {
                _values[key] = value;
            }

            public void DeleteKey(string key)
            {
                _values.Remove(key);
            }

            public void Save()
            {
            }
        }

        private sealed class CapturingLogHandler : ILogHandler, IDisposable
        {
            private readonly ILogHandler _previous;
            private readonly List<CapturedLog> _logs = new List<CapturedLog>();

            public CapturingLogHandler()
            {
                _previous = Debug.unityLogger.logHandler;
                Debug.unityLogger.logHandler = this;
            }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                var message = args == null || args.Length == 0 ? format : string.Format(format, args);
                _logs.Add(new CapturedLog(logType, message));
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                _logs.Add(new CapturedLog(LogType.Exception, exception?.Message ?? string.Empty));
            }

            public void AssertErrorContains(string expected)
            {
                foreach (var log in _logs)
                {
                    if (log.Type == LogType.Error && log.Message.Contains(expected))
                        return;
                }

                Assert.Fail($"Expected error log containing '{expected}'.");
            }

            public void Dispose()
            {
                Debug.unityLogger.logHandler = _previous;
            }
        }

        private readonly struct CapturedLog
        {
            public CapturedLog(LogType type, string message)
            {
                Type = type;
                Message = message;
            }

            public LogType Type { get; }
            public string Message { get; }
        }
    }
}
