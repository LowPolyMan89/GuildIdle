using System;

namespace GuildIdle.Player
{
    public interface IRuntimeConfigLifecycle
    {
        bool IsLoaded { get; }
        event Action Loaded;
        event Action<string> LoadFailed;
    }

    public sealed class PlayerBootstrapService : IDisposable
    {
        private readonly IRuntimeConfigLifecycle _configs;
        private readonly Func<bool> _isPlayerLoaded;
        private readonly Func<bool> _loadPlayer;
        private readonly Action<string> _handleConfigLoadFailed;
        private bool _started;

        public PlayerBootstrapService(
            IRuntimeConfigLifecycle configs,
            Func<bool> isPlayerLoaded,
            Func<bool> loadPlayer,
            Action<string> handleConfigLoadFailed)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _isPlayerLoaded = isPlayerLoaded ?? throw new ArgumentNullException(nameof(isPlayerLoaded));
            _loadPlayer = loadPlayer ?? throw new ArgumentNullException(nameof(loadPlayer));
            _handleConfigLoadFailed = handleConfigLoadFailed ?? throw new ArgumentNullException(nameof(handleConfigLoadFailed));
        }

        public void Start()
        {
            if (_started)
                return;

            _started = true;
            _configs.Loaded += OnConfigsLoaded;
            _configs.LoadFailed += OnConfigsLoadFailed;

            if (_configs.IsLoaded)
                OnConfigsLoaded();
        }

        public void Dispose()
        {
            if (!_started)
                return;

            _configs.Loaded -= OnConfigsLoaded;
            _configs.LoadFailed -= OnConfigsLoadFailed;
            _started = false;
        }

        private void OnConfigsLoaded()
        {
            if (!_isPlayerLoaded())
                _loadPlayer();
        }

        private void OnConfigsLoadFailed(string error)
        {
            _handleConfigLoadFailed(error);
        }
    }
}
