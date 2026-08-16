namespace GuildIdle.UI.Core
{
    public interface IUIOpenArgs
    {
    }

    public interface IUIOpenArgsReceiver<in TArgs>
        where TArgs : IUIOpenArgs
    {
        void ApplyOpenArgs(TArgs args);
    }

    public interface IUIState
    {
    }

    public interface IUIStateView<in TState>
        where TState : IUIState
    {
        void Render(TState state);
    }
}
