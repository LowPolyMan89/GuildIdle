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
            Assert.That(state.GetHeroInSlot(0), Is.EqualTo("ren"));
            Assert.That(state.GetItem("item_wooden_club"), Is.EqualTo(1));
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
                        new HeroConfigDto { heroId = "ren", enabled = true }
                    }
                },
                new ActivitiesRuntimeConfigDto
                {
                    activities = new[]
                    {
                        new ActivityConfigDto { id = "starter_hero_available" },
                        new ActivityConfigDto { id = "combat_first_map_node" }
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
                null,
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
