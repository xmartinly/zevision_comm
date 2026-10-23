using System;
using System.IO;
using Xunit;
using Zevision.Storage;

namespace Zevision.Tests
{
    /// <summary>
    /// 仪器配置文件目录改名为 <c>configs</c>:1.1.0~1.2.0 的 <c>recipes</c>、1.1.0 之前的 <c>recipe</c> 中的文件随目录移过去;
    /// <c>recipes</c> 之后是工艺编辑的目录。
    /// </summary>
    public sealed class AppFoldersTests : IDisposable
    {
        private readonly string _base = Path.Combine(Path.GetTempPath(), "ZevisionAppFolders_" + Guid.NewGuid().ToString("N"));

        public AppFoldersTests() => Directory.CreateDirectory(_base);

        public void Dispose() => Directory.Delete(_base, recursive: true);

        [Theory]
        [InlineData("recipes")] // 1.1.0~1.2.0
        [InlineData("recipe")]  // 1.1.0 之前
        public void MoveLegacyConfigFolder_OldFolderOnly_IsRenamedWithItsFiles(string legacy)
        {
            Directory.CreateDirectory(Path.Combine(_base, legacy));
            File.WriteAllText(Path.Combine(_base, legacy, "IMC300_a.json"), "{}");

            AppFolders.MoveLegacyConfigFolder(_base);

            Assert.False(Directory.Exists(Path.Combine(_base, legacy)));
            Assert.True(File.Exists(Path.Combine(_base, "configs", "IMC300_a.json")));
        }

        [Fact]
        public void MoveLegacyConfigFolder_BothOldFolders_RenamesRecipesKeepsRecipe()
        {
            Directory.CreateDirectory(Path.Combine(_base, "recipe"));
            Directory.CreateDirectory(Path.Combine(_base, "recipes"));
            File.WriteAllText(Path.Combine(_base, "recipe", "old.json"), "{}");
            File.WriteAllText(Path.Combine(_base, "recipes", "newer.json"), "{}");

            AppFolders.MoveLegacyConfigFolder(_base);

            Assert.True(File.Exists(Path.Combine(_base, "configs", "newer.json")));
            Assert.True(File.Exists(Path.Combine(_base, "recipe", "old.json")));
            Assert.False(Directory.Exists(Path.Combine(_base, "recipes")));
        }

        [Fact]
        public void MoveLegacyConfigFolder_ConfigsExists_LeavesEverythingAlone()
        {
            Directory.CreateDirectory(Path.Combine(_base, "configs"));
            Directory.CreateDirectory(Path.Combine(_base, "recipes"));
            File.WriteAllText(Path.Combine(_base, "recipes", "old.json"), "{}");

            AppFolders.MoveLegacyConfigFolder(_base);

            Assert.True(File.Exists(Path.Combine(_base, "recipes", "old.json")));
            Assert.Empty(Directory.GetFiles(Path.Combine(_base, "configs")));
        }

        [Fact]
        public void MoveLegacyConfigFolder_RecipesHoldsProcesses_IsNotRenamed()
        {
            // configs 被删除后:recipes 已是工艺编辑的目录,不能当作旧的配置文件目录。
            var process = Path.Combine(_base, "recipes", DesignFileStore.ProcessFolder, "AR.json");
            Directory.CreateDirectory(Path.GetDirectoryName(process)!);
            File.WriteAllText(process, "{}");

            AppFolders.MoveLegacyConfigFolder(_base);

            Assert.True(File.Exists(process));
            Assert.False(Directory.Exists(Path.Combine(_base, "configs")));
        }

        [Fact]
        public void MoveLegacyConfigFolder_NoOldFolder_CreatesNothing()
        {
            AppFolders.MoveLegacyConfigFolder(_base);

            Assert.Empty(Directory.GetFileSystemEntries(_base));
        }

        [Fact]
        public void Folders_ConfigsForConfigurations_RecipesForProcesses()
        {
            Assert.Equal("configs", new DirectoryInfo(AppFolders.GetConfigFolder()).Name);
            Assert.Equal("recipes", new DirectoryInfo(AppFolders.GetDesignFolder()).Name);
        }
    }
}
