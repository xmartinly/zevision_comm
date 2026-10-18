
namespace Zevision.Core.Settings
{
    /// <summary>
    /// 读写持久化的通讯配置。
    /// </summary>
    public interface IPreferencesStore
    {
        /// <summary>
        /// 读取配置。文件不存在 (首次运行) 时成功返回默认值;文件损坏或无法读取时返回失败,调用方应使用默认值并提示。
        /// </summary>
        Result<ConnectionPreferences> Load();

        /// <summary>
        /// 保存配置。
        /// </summary>
        Result Save(ConnectionPreferences preferences);
    }
}
