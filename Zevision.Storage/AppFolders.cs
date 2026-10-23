using System;
using System.IO;

namespace Zevision.Storage
{
    /// <summary>
    /// 程序数据位置:通讯配置 (settings.json)、仪器配置文件 (configs)、工艺编辑的材料和工艺 (recipes)、日志 (logs)。
    /// </summary>
    /// <remarks>
    /// 优先使用软件目录;软件装在 Program Files 等无写权限的位置时,改用 <c>文档\Zevision</c>。目录不存在时创建。
    /// </remarks>
    public static class AppFolders
    {
        /// <summary>仪器配置文件 (保存/恢复配置) 目录的名称。</summary>
        internal const string ConfigFolderName = "configs";

        /// <summary>工艺编辑的文件目录的名称;1.1.0~1.2.0 中是仪器配置文件目录。</summary>
        internal const string RecipeFolderName = "recipes";

        /// <summary>1.1.0 之前仪器配置文件目录的名称。</summary>
        internal const string LegacyRecipeFolderName = "recipe";

        /// <summary>通讯配置文件路径。</summary>
        public static string GetSettingsFilePath() => Path.Combine(GetBaseFolder(), "settings.json");

        /// <summary>仪器配置文件目录 (<c>configs</c>)。所用位置中旧版本的配置文件目录先改名为 <c>configs</c> (<see cref="MoveLegacyConfigFolder"/>)。</summary>
        public static string GetConfigFolder() => GetSubFolder(ConfigFolderName, MoveLegacyConfigFolder);

        /// <summary>日志目录 (收发记录、error.txt)。</summary>
        public static string GetLogsFolder() => GetSubFolder("logs");

        /// <summary>
        /// 工艺编辑的文件目录 (<c>recipes</c>,材料和工艺)。旧版本的仪器配置文件也在 <c>recipes</c> 中:取用前同样先把它改名为 <c>configs</c>,
        /// 工艺文件不会与旧的配置文件混在一起。
        /// </summary>
        public static string GetDesignFolder() => GetSubFolder(RecipeFolderName, MoveLegacyConfigFolder);

        private static string GetBaseFolder()
        {
            var preferred = AppDomain.CurrentDomain.BaseDirectory;
            return IsWritable(preferred) ? preferred : GetFallbackBase();
        }

        /// <summary>子目录:优先在程序目录下,不可写时在 <c>文档\Zevision</c> 下,都不可写时用临时目录。</summary>
        /// <param name="name">子目录名。</param>
        /// <param name="prepare">尝试某个位置之前对该位置 (程序目录或 <c>文档\Zevision</c>) 的处理;只对实际尝试的位置调用。</param>
        private static string GetSubFolder(string name, Action<string>? prepare = null)
        {
            var preferredBase = AppDomain.CurrentDomain.BaseDirectory;
            prepare?.Invoke(preferredBase);
            var preferred = Path.Combine(preferredBase, name);
            if (IsWritable(preferred))
            {
                return preferred;
            }

            var fallbackBase = GetFallbackBase();
            prepare?.Invoke(fallbackBase);
            var fallback = Path.Combine(fallbackBase, name);
            return IsWritable(fallback) ? fallback : Path.GetTempPath();
        }

        /// <summary>
        /// 把旧版本的仪器配置文件目录改名为 <c>configs</c>,已保存的配置文件仍在默认位置。<c>configs</c> 已存在时什么也不做;
        /// 否则 1.1.0~1.2.0 的 <c>recipes</c> 改名为 <c>configs</c> (其中有 material、film、layer、process 目录时它已是工艺编辑的目录,不改名),
        /// 没有 <c>recipes</c> 时 1.1.0 之前的 <c>recipe</c> 改名为 <c>configs</c>。
        /// </summary>
        /// <remarks>
        /// 改名失败 (目录只读,或其中的文件被其他程序打开) 时保留旧目录:文件仍可在打开对话框中找到,新文件保存到 <c>configs</c>。
        /// 这里没有可写的日志 (Storage 的静态工具),也不影响程序的其他功能,所以不报告。
        /// <c>recipe</c> 和 <c>recipes</c> 都存在时只改名 <c>recipes</c>,<c>recipe</c> 保留 (与 1.1.0 相同)。
        /// </remarks>
        /// <param name="baseFolder">程序目录或 <c>文档\Zevision</c>。</param>
        internal static void MoveLegacyConfigFolder(string baseFolder)
        {
            var current = Path.Combine(baseFolder, ConfigFolderName);
            if (Directory.Exists(current))
            {
                return;
            }

            var recipes = Path.Combine(baseFolder, RecipeFolderName);
            var legacy = Directory.Exists(recipes) && !DesignFileStore.HasDesignFolders(recipes)
                ? recipes
                : Path.Combine(baseFolder, LegacyRecipeFolderName);
            if (!Directory.Exists(legacy))
            {
                return;
            }

            try
            {
                Directory.Move(legacy, current);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 见 remarks:保留旧目录。
            }
        }

        private static string GetFallbackBase() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Zevision");

        private static bool IsWritable(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, ".write_test_" + Guid.NewGuid().ToString("N"));
                using (File.Create(probe, 1, FileOptions.DeleteOnClose))
                {
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
