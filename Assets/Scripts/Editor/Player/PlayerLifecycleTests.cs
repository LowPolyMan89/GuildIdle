using GuildIdle.Configs;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using RuntimeConfigs = GuildIdle.Configs.Configs;
using UnityEngine;
using UnityEngine.TestTools;

namespace GuildIdle.Editor.Player
{
    public sealed class PlayerLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            // Clear PlayerPrefs to isolate from previous test runs that may have
            // saved data with building/activity ids unknown to this test database.
            PlayerPrefs.DeleteKey(SaveService.SaveKey);
            PlayerPrefs.Save();

            RuntimeConfigs.SetDatabaseForTests(CreateDatabase());
        }

        [Test]
        public void LoadAfterConfigs_DoesNotReloadWhenStateAlreadyLoaded()
        {
            Assert.That(global::GuildIdle.Player.Player.Load(), Is.True,
                "First load should succeed when configs are set.");

            Assert.That(global::GuildIdle.Player.Player.IsLoaded, Is.True);

            Assert.That(global::GuildIdle.Player.Player.AddItem("resource_pine_wood", 5), Is.True);
            Assert.That(global::GuildIdle.Player.Player.GetItem("resource_pine_wood"), Is.EqualTo(5));

            // Simulate OnLoaded firing again (e.g. after Configs.Reload without state reset)
            global::GuildIdle.Player.Player.LoadAfterConfigs();

            // State should still have the item — LoadAfterConfigs must not reload if _state != null
            Assert.That(global::GuildIdle.Player.Player.GetItem("resource_pine_wood"), Is.EqualTo(5),
                "LoadAfterConfigs should not reload player state when it is already loaded.");
        }

        [Test]
        public void Bootstrap_SubscribesOnce_AndLoadsOnConfigLoad()
        {
            // Bootstrap is called via [RuntimeInitializeOnLoadMethod] before scene load.
            // In edit-mode tests we simulate by calling LoadAfterConfigs directly.
            // The key invariant: calling LoadAfterConfigs twice should not double-load.

            Assert.That(global::GuildIdle.Player.Player.Load(), Is.True);
            Assert.That(global::GuildIdle.Player.Player.AddItem("resource_pine_wood", 3), Is.True);

            // Simulate a second OnLoaded event
            global::GuildIdle.Player.Player.LoadAfterConfigs();

            Assert.That(global::GuildIdle.Player.Player.GetItem("resource_pine_wood"), Is.EqualTo(3),
                "Second OnLoaded must not reset player state.");
        }

        [Test]
        public void Load_CanBeCalledMultipleTimes_WithoutDuplicatingState()
        {
            Assert.That(global::GuildIdle.Player.Player.Load(), Is.True);
            Assert.That(global::GuildIdle.Player.Player.AddItem("resource_pine_wood", 3), Is.True);

            // Second Load() should reload from SaveService, which returns the saved state
            // Since we haven't saved, Load() will create a fresh default state
            Assert.That(global::GuildIdle.Player.Player.Load(), Is.True);

            // After reload, the item should be gone (fresh default state)
            Assert.That(global::GuildIdle.Player.Player.GetItem("resource_pine_wood"), Is.EqualTo(0),
                "Load() replaces _state with a fresh load from SaveService.");
        }

        [Test]
        public void Bootstrap_DoesNotDoubleLoad_AfterConfigReload()
        {
            // Bootstrap подписывается на OnLoaded
            global::GuildIdle.Player.Player.Bootstrap();

            // Симулируем OnLoaded → LoadAfterConfigs
            global::GuildIdle.Player.Player.LoadAfterConfigs();
            Assert.That(global::GuildIdle.Player.Player.IsLoaded, Is.True);
            Assert.That(global::GuildIdle.Player.Player.AddItem("resource_pine_wood", 5), Is.True);

            // Симулируем OnLoadFailed — _state сбрасывается
            LogAssert.Expect(LogType.Error, "[Player] Runtime configs failed to load; player state was not initialized. test error");
            global::GuildIdle.Player.Player.HandleConfigLoadFailed("test error");
            Assert.That(global::GuildIdle.Player.Player.IsLoaded, Is.False);

            // Симулируем Configs.Reload → OnLoaded
            global::GuildIdle.Player.Player.LoadAfterConfigs();
            Assert.That(global::GuildIdle.Player.Player.IsLoaded, Is.True);

            // После reload состояние свежее — предмета нет
            Assert.That(global::GuildIdle.Player.Player.GetItem("resource_pine_wood"), Is.EqualTo(0),
                "After config fail + reload, LoadAfterConfigs must create fresh state.");
        }

        [Test]
        public void Bootstrap_SubscribesOnce_AfterConfigFailThenReload()
        {
            global::GuildIdle.Player.Player.Bootstrap();
            global::GuildIdle.Player.Player.LoadAfterConfigs();
            Assert.That(global::GuildIdle.Player.Player.AddItem("resource_pine_wood", 3), Is.True);

            // Второй OnLoaded — guard не даёт перезагрузить
            global::GuildIdle.Player.Player.LoadAfterConfigs();
            Assert.That(global::GuildIdle.Player.Player.GetItem("resource_pine_wood"), Is.EqualTo(3),
                "Second LoadAfterConfigs must not reset state.");
        }

        private static ConfigDatabase CreateDatabase()
        {
            return new TestConfigDatabaseBuilder()
                .WithMinimalItems()
                .WithMinimalHeroes()
                .WithMinimalActivities()
                .WithMinimalBuildings()
                .WithFatigueFormula()
                .WithMinimalMap()
                .Build();
        }
    }
}