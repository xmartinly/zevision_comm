using System;
using System.IO;
using Zevision.Core;
using Zevision.Core.Settings;

namespace Zevision.Storage
{
    /// <summary>
    /// 把通讯配置保存为软件目录下的 <c>settings.json</c> (见 <see cref="AppFolders.GetSettingsFilePath"/>)。
    /// </summary>
    public sealed class JsonFilePreferencesStore : IPreferencesStore
    {
        private readonly string _path;

        /// <summary>
        /// 创建存储。
        /// </summary>
        /// <param name="path">配置文件路径。</param>
        public JsonFilePreferencesStore(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException(Texts.PathEmpty, nameof(path));
            }

            _path = path;
        }

        /// <inheritdoc />
        public Result<ConnectionPreferences> Load()
        {
            if (!File.Exists(_path))
            {
                return Result<ConnectionPreferences>.Ok(new ConnectionPreferences());
            }

            try
            {
                var preferences = JsonFile.Read<ConnectionPreferences>(_path);
                if (preferences == null)
                {
                    return Result<ConnectionPreferences>.Fail(Texts.SettingsFileEmpty(_path));
                }

                preferences.PortName = preferences.PortName ?? string.Empty;
                return Result<ConnectionPreferences>.Ok(preferences);
            }
            catch (Exception ex) when (JsonFile.IsExpectedFailure(ex))
            {
                return Result<ConnectionPreferences>.Fail(Texts.SettingsReadFailed(_path, ex.Message));
            }
        }

        /// <inheritdoc />
        public Result Save(ConnectionPreferences preferences)
        {
            if (preferences is null)
            {
                throw new ArgumentNullException(nameof(preferences));
            }

            try
            {
                JsonFile.Write(_path, preferences);
                return Result.Ok();
            }
            catch (Exception ex) when (JsonFile.IsExpectedFailure(ex))
            {
                return Result.Fail(Texts.SettingsSaveFailed(ex.Message));
            }
        }
    }
}
