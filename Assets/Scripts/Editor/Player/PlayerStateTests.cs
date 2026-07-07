using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Player
{
    public sealed class PlayerStateTests
    {
        [SetUp]
        public void SetUp()
        {
            RuntimeConfigs.SetDatabaseForTests(CreateDatabase());
        }

        [Test]
        public void CreateDefault_AppliesStarterHeroAndEquipment()
        {
            var state = PlayerState.CreateDefault();

            Assert.That(state.IsHeroUnlocked("ren"), Is.True);
            Assert.That(state.HasHero("ren"), Is.True);
            Assert.That(state.HasHeroState("ren"), Is.True);
            Assert.That(state.GetHeroInSlot(0), Is.EqualTo("ren"));
            Assert.That(state.GetHeroMaxFatigue("ren"), Is.GreaterThan(0));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren")));
            Assert.That(state.GetItem("item_wooden_club"), Is.EqualTo(1));

            var saveData = state.ToSaveData();
            Assert.That(saveData.heroes, Has.Length.EqualTo(1));
            Assert.That(saveData.heroes[0].heroId, Is.EqualTo("ren"));
        }

        [Test]
        public void Items_AddAndSpendWithoutGoingNegative()
        {
            var state = PlayerState.CreateDefault();

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
            var state = PlayerState.CreateDefault();

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
            var state = PlayerState.CreateDefault();

            Assert.That(state.AddHero("ren"), Is.False);

            var saveData = state.ToSaveData();
            Assert.That(saveData.acquiredHeroes, Is.EqualTo(new[] { "ren" }));
            Assert.That(saveData.unlockedHeroes, Is.EqualTo(new[] { "ren" }));
        }

        [Test]
        public void BuildingLevel_RequiresUnlockedBuilding()
        {
            var state = PlayerState.CreateDefault();

            using (var logs = new CapturingLogHandler())
            {
                Assert.That(state.SetBuildingLevel("building_watchtower", 1), Is.False);
                logs.AssertErrorContains("[PlayerState] Cannot set level for locked building 'building_watchtower'.");
            }

            Assert.That(state.GetBuildingLevel("building_watchtower"), Is.EqualTo(0));

            Assert.That(state.UnlockBuilding("building_watchtower"), Is.True);
            Assert.That(state.GetBuildingLevel("building_watchtower"), Is.EqualTo(1));
            Assert.That(state.SetBuildingLevel("building_watchtower", 1), Is.True);
        }

        [Test]
        public void LocationAndActivity_CanBeUnlockedAndCompleted()
        {
            var state = PlayerState.CreateDefault();

            Assert.That(state.UnlockLocation("old_wolf_den_1_1"), Is.True);
            Assert.That(state.IsLocationUnlocked("old_wolf_den_1_1"), Is.True);
            Assert.That(state.CompleteActivity("combat_first_map_node"), Is.True);
            Assert.That(state.IsActivityCompleted("combat_first_map_node"), Is.True);
        }

        [Test]
        public void UnknownId_DoesNotMutateState()
        {
            var state = PlayerState.CreateDefault();
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
            var state = PlayerState.CreateDefault();
            var maxFatigue = state.GetHeroMaxFatigue("ren");

            Assert.That(state.SpendHeroFatigue("ren", 5), Is.True);
            Assert.That(state.AddHeroSkillExp("ren", "skill_gathering", 150), Is.True);
            Assert.That(state.SetHeroBusy("ren", "exec_1"), Is.True);

            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);
            var restored = SaveService.Load(storage);

            Assert.That(restored.GetHeroFatigue("ren"), Is.EqualTo(maxFatigue - 5));
            Assert.That(restored.GetHeroSkillExp("ren", "skill_gathering"), Is.EqualTo(150));
            Assert.That(restored.GetHeroSkillLevel("ren", "skill_gathering"), Is.EqualTo(2));
            Assert.That(restored.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo("exec_1"));
        }

        [Test]
        public void Load_V1SaveWithoutHeroStatesHydratesAcquiredHeroes()
        {
            var state = new PlayerState(new SaveData
            {
                saveVersion = 1,
                unlockedHeroes = new[] { "ren" },
                acquiredHeroes = new[] { "ren" },
                heroSlots = new[] { new HeroSlotSaveEntry { slotIndex = 0, heroId = "ren" } }
            });

            Assert.That(state.HasHeroState("ren"), Is.True);
            Assert.That(state.GetHeroInSlot(0), Is.EqualTo("ren"));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren")));
            Assert.That(state.ToSaveData().saveVersion, Is.EqualTo(SaveData.CurrentSaveVersion));
        }

        [Test]
        public void HeroBusy_IsIdempotentForSameExecutionAndRejectsDifferentExecution()
        {
            var state = PlayerState.CreateDefault();

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
                state = SaveService.Load(storage);
                logs.AssertErrorContains("[SaveService] Failed to load player save JSON. Creating default save.");
            }

            Assert.That(state.HasHero("ren"), Is.True);
            Assert.That(state.GetHeroInSlot(0), Is.EqualTo("ren"));
            Assert.That(storage.HasKey(SaveService.SaveKey), Is.True);
            Assert.That(storage.GetString(SaveService.SaveKey, string.Empty), Does.Contain("\"heroId\":\"ren\""));
        }

        [Test]
        public void SaveService_LoadCreatesDefaultPlayerPrefsJsonWhenMissing()
        {
            var storage = new MemorySaveStorage();

            var state = SaveService.Load(storage);

            Assert.That(state.HasHero("ren"), Is.True);
            Assert.That(storage.HasKey(SaveService.SaveKey), Is.True);
            Assert.That(storage.GetString(SaveService.SaveKey, string.Empty), Does.Contain("\"heroId\":\"ren\""));
        }

        private static ConfigDatabase CreateDatabase()
        {
            return new ConfigDatabase(
                new ItemsRuntimeConfigDto
                {
                    resources = new[]
                    {
                        new ResourceConfigDto { id = "resource_pine_wood", kind = "resource" }
                    },
                    equipmentWeapons = new[]
                    {
                        new EquipmentWeaponConfigDto { id = "item_wooden_club", kind = "equipment" }
                    },
                    currencies = new[]
                    {
                        new CurrencyConfigDto { currencyId = "gold_id" }
                    }
                },
                new HeroesRuntimeConfigDto
                {
                    heroes = new[]
                    {
                        new HeroConfigDto
                        {
                            heroId = "ren",
                            enabled = true,
                            baseStats = new HeroBaseStatsDto { endurance = 5 }
                        }
                    }
                },
                new ActivitiesRuntimeConfigDto
                {
                    activities = new[]
                    {
                        new ActivityConfigDto { id = "starter_hero_available" },
                        new ActivityConfigDto { id = "combat_first_map_node" }
                    },
                    skills = new[]
                    {
                        new SkillConfigDto { skillId = "skill_gathering" }
                    },
                    skillsProgression = new[]
                    {
                        new SkillProgressionConfigDto { level = 1, totalExpRequired = 0 },
                        new SkillProgressionConfigDto { level = 2, totalExpRequired = 100 }
                    },
                    rewards = new[]
                    {
                        new ActivityRewardConfigDto
                        {
                            activityId = "starter_hero_available",
                            rewardType = "Hero",
                            targetId = "ren",
                            min = 1,
                            max = 1,
                            chance = 100
                        },
                        new ActivityRewardConfigDto
                        {
                            activityId = "starter_hero_available",
                            rewardType = "Equipment",
                            targetId = "item_wooden_club",
                            min = 1,
                            max = 1,
                            chance = 100
                        }
                    }
                },
                new BuildingsRuntimeConfigDto
                {
                    buildings = new[]
                    {
                        new BuildingConfigDto { buildingId = "building_watchtower", levels = 1 }
                    }
                },
                null,
                new FormulaRuntimeConfigDto
                {
                    heroDerivedStats = new[]
                    {
                        new HeroDerivedStatConfigDto
                        {
                            formulaId = "hero_max_fatigue",
                            derivedStatId = "max_fatigue",
                            baseValue = 100,
                            primaryStat = "Endurance",
                            primaryStatMultiplier = 4,
                            levelMultiplier = 1,
                            rounding = "Round",
                            enabled = true
                        }
                    }
                },
                null,
                new MapRuntimeConfigDto
                {
                    mapLocations = new[]
                    {
                        new MapLocationConfigDto { locationId = "old_wolf_den_1_1" }
                    }
                },
                null,
                null);
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
