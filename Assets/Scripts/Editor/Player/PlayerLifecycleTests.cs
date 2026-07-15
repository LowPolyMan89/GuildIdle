using System;
using GuildIdle.Configs;
using GuildIdle.Player;
using NUnit.Framework;

namespace GuildIdle.Editor.Player
{
    public sealed class PlayerLifecycleTests
    {
        private static readonly HeroStatsService HeroStats = new HeroStatsService(new EmptyHeroStatsConfigProvider());

        [Test]
        public void Start_WhenConfigsAreAlreadyLoaded_LoadsPlayerOnce()
        {
            var lifecycle = new FakeRuntimeConfigLifecycle { IsLoaded = true };
            PlayerState state = null;
            var loadCount = 0;

            using var service = CreateService(lifecycle, () => state != null, () =>
            {
                loadCount++;
                state = new PlayerState(new SaveData(), HeroStats);
                return true;
            });

            service.Start();
            service.Start();

            Assert.That(loadCount, Is.EqualTo(1));
            Assert.That(state, Is.Not.Null);
        }

        [Test]
        public void LoadedEvent_RepeatedWithoutFailure_DoesNotReloadPlayer()
        {
            var lifecycle = new FakeRuntimeConfigLifecycle();
            PlayerState state = null;
            var loadCount = 0;

            using var service = CreateService(lifecycle, () => state != null, () =>
            {
                loadCount++;
                state = new PlayerState(new SaveData(), HeroStats);
                return true;
            });
            service.Start();

            lifecycle.RaiseLoaded();
            var firstState = state;
            lifecycle.RaiseLoaded();

            Assert.That(loadCount, Is.EqualTo(1));
            Assert.That(state, Is.SameAs(firstState));
        }

        [Test]
        public void LoadFailedThenLoaded_ResetsAndLoadsFreshPlayerOnce()
        {
            var lifecycle = new FakeRuntimeConfigLifecycle();
            PlayerState state = null;
            var loadCount = 0;
            string failure = null;

            using var service = new PlayerBootstrapService(
                lifecycle,
                () => state != null,
                () =>
                {
                    loadCount++;
                    state = new PlayerState(new SaveData(), HeroStats);
                    return true;
                },
                error =>
                {
                    failure = error;
                    state = null;
                });
            service.Start();

            lifecycle.RaiseLoaded();
            var firstState = state;
            lifecycle.RaiseLoadFailed("test error");
            lifecycle.RaiseLoaded();
            lifecycle.RaiseLoaded();

            Assert.That(failure, Is.EqualTo("test error"));
            Assert.That(loadCount, Is.EqualTo(2));
            Assert.That(state, Is.Not.Null);
            Assert.That(state, Is.Not.SameAs(firstState));
        }

        [Test]
        public void Dispose_UnsubscribesFromLifecycleEvents()
        {
            var lifecycle = new FakeRuntimeConfigLifecycle();
            var loadCount = 0;
            var service = CreateService(lifecycle, () => false, () =>
            {
                loadCount++;
                return true;
            });
            service.Start();

            service.Dispose();
            lifecycle.RaiseLoaded();

            Assert.That(loadCount, Is.Zero);
        }

        private static PlayerBootstrapService CreateService(
            IRuntimeConfigLifecycle lifecycle,
            Func<bool> isPlayerLoaded,
            Func<bool> loadPlayer)
        {
            return new PlayerBootstrapService(lifecycle, isPlayerLoaded, loadPlayer, _ => { });
        }

        private sealed class FakeRuntimeConfigLifecycle : IRuntimeConfigLifecycle
        {
            public bool IsLoaded { get; set; }
            public event Action Loaded;
            public event Action<string> LoadFailed;

            public void RaiseLoaded()
            {
                IsLoaded = true;
                Loaded?.Invoke();
            }

            public void RaiseLoadFailed(string error)
            {
                IsLoaded = false;
                LoadFailed?.Invoke(error);
            }
        }

        private sealed class EmptyHeroStatsConfigProvider : IHeroStatsConfigProvider
        {
            public HeroGrowthConfigDto[] HeroGrowth => Array.Empty<HeroGrowthConfigDto>();
            public SkillProgressionConfigDto[] SkillProgression => Array.Empty<SkillProgressionConfigDto>();

            public bool TryGetHero(string heroId, out HeroConfigDto hero)
            {
                hero = null;
                return false;
            }

            public bool TryGetFormula(string formulaId, out FormulaConfigDto formula)
            {
                formula = null;
                return false;
            }
        }
    }
}
