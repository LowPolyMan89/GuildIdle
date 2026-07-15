using System.Reflection;
using GuildIdle.Activities;
using GuildIdle.Core;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Player
{
    public sealed class PlayerRuntimeCompositionTests
    {
        [Test]
        public void PlayerStateFactoryGraph_IsCachedUntilConfigFailureInvalidatesIt()
        {
            RuntimeConfigs.SetDatabaseForTests(new TestConfigDatabaseBuilder()
                .WithFullPlayerStateTestData()
                .Build());

            var getFactory = typeof(PlayerRuntimeComposition).GetMethod(
                "GetPlayerStateFactory",
                BindingFlags.NonPublic | BindingFlags.Static);
            var invalidateFactory = typeof(PlayerRuntimeComposition).GetMethod(
                "InvalidatePlayerStateFactory",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(getFactory, Is.Not.Null);
            Assert.That(invalidateFactory, Is.Not.Null);

            try
            {
                invalidateFactory.Invoke(null, null);
                var first = getFactory.Invoke(null, null);
                var repeated = getFactory.Invoke(null, null);

                Assert.That(repeated, Is.SameAs(first));

                invalidateFactory.Invoke(null, null);
                var afterFailure = getFactory.Invoke(null, null);

                Assert.That(afterFailure, Is.Not.SameAs(first));
                Assert.That(getFactory.Invoke(null, null), Is.SameAs(afterFailure));
            }
            finally
            {
                invalidateFactory.Invoke(null, null);
            }
        }

        [Test]
        public void StageQuestRuntimeFactory_UsesProvidedPlayerState()
        {
            var database = new TestConfigDatabaseBuilder()
                .WithFullPlayerStateTestData()
                .Build();
            RuntimeConfigs.SetDatabaseForTests(database);
            var state = TestPlayerComposition.CreatePlayerStateFactory(database).CreateDefault();

            var runtime = PlayerRuntimeComposition.CreateStageQuestRuntimeService(state);

            Assert.That(runtime.GetSnapshot().CurrentStage.StageId, Is.EqualTo("stage_arrival"));
        }

        [Test]
        public void ActivityRewardBatchDependency_IsExplicitInContracts()
        {
            Assert.That(typeof(IRewardBatchStore).IsAssignableFrom(typeof(IActivityPlayerState)), Is.True);
            Assert.That(typeof(IRewardBatchStore).IsAssignableFrom(typeof(PlayerState)), Is.True);
        }
    }
}
