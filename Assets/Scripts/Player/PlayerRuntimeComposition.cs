using System;
using GuildIdle.Activities;
using GuildIdle.Core;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Player
{
    public static class PlayerRuntimeComposition
    {
        public static ActivityRuntimeService CreateRuntimeService()
        {
            var state = Player.State;
            if (state == null)
                throw new System.InvalidOperationException("Player state is not loaded yet. Call Player.Load() or wait for config load.");

            return new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state));
        }

        public static ActivityRuntimeService CreateRuntimeService(PlayerState state)
        {
            return new ActivityRuntimeService(
                state ?? throw new System.ArgumentNullException(nameof(state)),
                new PlayerStateActivityAdapter(state));
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
