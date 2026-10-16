using System;
using System.IO;

namespace Zevision.Services
{
    /// <summary>
    /// 程序数据位置:通讯配置 (settings.json)、仪器配置文件 (recipe)、日志 (logs)。
    /// </summary>
    /// <remarks>
    /// 优先使用软件目录;软件装在 Program Files 等无写权限的位置时,改用 <c>文档\Zevision</c>。目录不存在时创建。
    /// </remarks>
    public static class AppFolders
    {
        /// <summary>通讯配置文件路径。</summary>
        public static string GetSettingsFilePath() => Path.Combine(GetBaseFolder(), "settings.json");

        /// <summary>仪器配置文件目录。</summary>
        public static string GetRecipeFolder() => GetSubFolder("recipe");

        /// <summary>日志目录 (收发记录、error.txt)。</summary>
        public static string GetLogsFolder() => GetSubFolder("logs");

        private static string GetBaseFolder()
        {
            var preferred = AppDomain.CurrentDomain.BaseDirectory;
            return IsWritable(preferred) ? preferred : GetFallbackBase();
        }

        private static string GetSubFolder(string name)
        {
            var preferred = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            if (IsWritable(preferred))
            {
                return preferred;
            }

            var fallback = Path.Combine(GetFallbackBase(), name);
            return IsWritable(fallback) ? fallback : Path.GetTempPath();
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
