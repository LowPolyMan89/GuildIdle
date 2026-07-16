using System;
using GuildIdle.Activities;
using GuildIdle.Core;
using GuildIdle.Progression;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Player
{
    public static class PlayerRuntimeComposition
    {
        private static readonly PlayerBootstrapDefinition BootstrapDefinition = new PlayerBootstrapDefinition("stage_arrival");
        private static PlayerStateFactory _playerStateFactory;

        public static ActivityRuntimeService CreateRuntimeService()
        {
            var state = Player.State;
            if (state == null)
                throw new System.InvalidOperationException("Player state is not loaded yet. Call Player.Load() or wait for config load.");

            return new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                eventSink: HandleActivityRuntimeEvent);
        }

        public static ActivityRuntimeService CreateRuntimeService(PlayerState state)
        {
            return new ActivityRuntimeService(
                state ?? throw new System.ArgumentNullException(nameof(state)),
                new PlayerStateActivityAdapter(state));
        }

        public static IStorageService CreateStorageService()
        {
            var state = Player.State;
            if (state == null)
                throw new InvalidOperationException("Player state is not loaded yet. Call Player.Load() or wait for config load.");
            return state.Storage;
        }

        public static IPendingResultService CreatePendingResultService()
        {
            var state = Player.State;
            if (state == null)
                throw new InvalidOperationException("Player state is not loaded yet. Call Player.Load() or wait for config load.");
            return state.PendingResults;
        }

        public static ProgressionRuntimeService CreateProgressionRuntimeService()
        {
            var state = Player.State;
            if (state == null)
                throw new InvalidOperationException("Player state is not loaded yet. Call Player.Load() or wait for config load.");

            return CreateProgressionRuntimeService(state);
        }

        public static ProgressionRuntimeService CreateProgressionRuntimeService(PlayerState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            var configs = new RepositoryProgressionConfigAdapter(RuntimeConfigs.Quests);
            var store = new PlayerStateProgressionAdapter(state);
            return new ProgressionRuntimeService(
                new QuestRuntimeService(configs, store),
                new StageProgressionService(configs, store),
                store,
                new RepositoryNonBuildTransitionAdapter(RuntimeConfigs.Buildings));
        }

        internal static PlayerBootstrapService CreateBootstrapService(
            Func<bool> isPlayerLoaded,
            Func<bool> loadPlayer,
            Action<string> handleConfigLoadFailed)
        {
            return new PlayerBootstrapService(
                new RuntimeConfigLifecycleAdapter(),
                isPlayerLoaded,
                loadPlayer,
                handleConfigLoadFailed);
        }

        internal static PlayerState LoadPlayerState(out SaveLoadOrigin origin)
        {
            return SaveService.Load(GetPlayerStateFactory(), null, out origin);
        }

        internal static PlayerState ResetPlayerState()
        {
            return SaveService.ResetSave(GetPlayerStateFactory());
        }

        internal static void InvalidatePlayerStateFactory()
        {
            _playerStateFactory = null;
        }

        private static void HandleActivityRuntimeEvent(ActivityRuntimeEvent runtimeEvent)
        {
            if (runtimeEvent == null || Player.Progression == null)
                return;
            if (string.Equals(runtimeEvent.eventType, ActivityRuntimeEventType.BuildingLevelChanged, StringComparison.Ordinal))
                Player.Progression.Handle(new BuildingLevelChanged(runtimeEvent.targetId, runtimeEvent.value));
            else if (string.Equals(runtimeEvent.eventType, ActivityRuntimeEventType.ActivityCompleted, StringComparison.Ordinal))
                Player.Progression.HandleActivityCompleted(runtimeEvent.targetId);
        }

        private static PlayerStateFactory GetPlayerStateFactory()
        {
            if (_playerStateFactory != null)
                return _playerStateFactory;

            var heroStatsConfigs = new RepositoryHeroStatsConfigAdapter(
                RuntimeConfigs.Heroes,
                RuntimeConfigs.Formulas,
                RuntimeConfigs.Activities);
            var bootstrapConfigs = new RepositoryPlayerBootstrapConfigAdapter(
                RuntimeConfigs.Items,
                RuntimeConfigs.Heroes,
                RuntimeConfigs.Activities,
                RuntimeConfigs.Buildings,
                RuntimeConfigs.Quests,
                RuntimeConfigs.Storage);
            var heroStats = new HeroStatsService(heroStatsConfigs);
            _playerStateFactory = new PlayerStateFactory(
                bootstrapConfigs,
                heroStats,
                BootstrapDefinition);
            return _playerStateFactory;
        }

        private sealed class RuntimeConfigLifecycleAdapter : IRuntimeConfigLifecycle
        {
            public bool IsLoaded => RuntimeConfigs.IsLoaded;

            public event Action Loaded
            {
                add => RuntimeConfigs.OnLoaded += value;
                remove => RuntimeConfigs.OnLoaded -= value;
            }

            public event Action<string> LoadFailed
            {
                add => RuntimeConfigs.OnLoadFailed += value;
                remove => RuntimeConfigs.OnLoadFailed -= value;
            }
        }
    }
}
