using GuildIdle.Activities;
using GuildIdle.Core;

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
    }
}