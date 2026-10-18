using System;
using System.Reflection;
using Zevision.Core.Localization;

namespace Zevision
{
    /// <summary>
    /// 程序名称与版本,来自程序集特性 (Properties/AssemblyInfo.cs)。
    /// </summary>
    internal static class AppInfo
    {
        /// <summary>产品名称。</summary>
        public const string ProductName = "Zevision";

        /// <summary>版本号,如 <c>0.9.6</c> (AssemblyInformationalVersion)。</summary>
        public static string Version { get; } =
            typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AppInfo).Assembly.GetName().Version.ToString(3);

        /// <summary>
        /// 写在每次运行的第一条错误记录之前:版本和运行环境,便于排查用户反馈的问题。
        /// </summary>
        public static string Description =>
            $"{ProductName} {Version}  |  {Environment.OSVersion}  |  .NET CLR {Environment.Version}  |  {Bitness}";

        private static string Bitness =>
            Environment.Is64BitProcess ? Lang.T("64 位进程", "64-bit process") : Lang.T("32 位进程", "32-bit process");
    }
}
