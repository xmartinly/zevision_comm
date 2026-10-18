using Zevision.Core;
using Zevision.Core.Settings;

namespace Zevision.Tests
{
    /// <summary>内存中的配置存储:加载给定的配置,记录保存的配置。</summary>
    internal sealed class MemoryPreferencesStore : IPreferencesStore
    {
        private readonly ConnectionPreferences _loaded;

        public MemoryPreferencesStore(ConnectionPreferences loaded) => _loaded = loaded;

        public ConnectionPreferences? Saved { get; private set; }

        public Result<ConnectionPreferences> Load() => Result<ConnectionPreferences>.Ok(_loaded);

        public Result Save(ConnectionPreferences preferences)
        {
            Saved = preferences;
            return Result.Ok();
        }
    }
}
