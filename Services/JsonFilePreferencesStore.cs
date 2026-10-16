using System;
using System.IO;
using Zevision.Localization;
using Zevision.Models;

namespace Zevision.Services
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
                throw new ArgumentException(Lang.T("路径不能为空。", "The path must not be empty."), nameof(path));
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
                    return Result<ConnectionPreferences>.Fail(Lang.T($"通讯配置文件 {_path} 内容为空,已使用默认值。", $"The settings file {_path} is empty; defaults are used."));
                }

                preferences.PortName = preferences.PortName ?? string.Empty;
                return Result<ConnectionPreferences>.Ok(preferences);
            }
            catch (Exception ex) when (JsonFile.IsExpectedFailure(ex))
            {
                return Result<ConnectionPreferences>.Fail(Lang.T($"无法读取通讯配置文件 {_path},已使用默认值: {ex.Message}", $"Cannot read the settings file {_path}; defaults are used: {ex.Message}"));
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
                return Result.Fail(Lang.T($"保存通讯配置失败: {ex.Message}", $"Saving the settings failed: {ex.Message}"));
            }
        }
    }
}
