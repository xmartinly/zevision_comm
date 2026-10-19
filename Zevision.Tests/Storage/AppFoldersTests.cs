using System;
using System.IO;
using Xunit;
using Zevision.Storage;

namespace Zevision.Tests
{
    /// <summary>仪器配置文件目录从 <c>recipe</c> 改名为 <c>recipes</c> (1.1.0):旧目录中的文件随目录移过去。</summary>
    public sealed class AppFoldersTests : IDisposable
    {
        private readonly string _base = Path.Combine(Path.GetTempPath(), "ZevisionAppFolders_" + Guid.NewGuid().ToString("N"));

        public AppFoldersTests() => Directory.CreateDirectory(_base);

        public void Dispose() => Directory.Delete(_base, recursive: true);

        [Fact]
        public void MoveLegacyRecipeFolder_OldFolderOnly_IsRenamedWithItsFiles()
        {
            Directory.CreateDirectory(Path.Combine(_base, "recipe"));
            File.WriteAllText(Path.Combine(_base, "recipe", "IMC300_a.json"), "{}");

            AppFolders.MoveLegacyRecipeFolder(_base);

            Assert.False(Directory.Exists(Path.Combine(_base, "recipe")));
            Assert.True(File.Exists(Path.Combine(_base, "recipes", "IMC300_a.json")));
        }

        [Fact]
        public void MoveLegacyRecipeFolder_NewFolderExists_LeavesBothAlone()
        {
            Directory.CreateDirectory(Path.Combine(_base, "recipe"));
            Directory.CreateDirectory(Path.Combine(_base, "recipes"));
            File.WriteAllText(Path.Combine(_base, "recipe", "old.json"), "{}");

            AppFolders.MoveLegacyRecipeFolder(_base);

            Assert.True(File.Exists(Path.Combine(_base, "recipe", "old.json")));
            Assert.Empty(Directory.GetFiles(Path.Combine(_base, "recipes")));
        }

        [Fact]
        public void MoveLegacyRecipeFolder_NoOldFolder_CreatesNothing()
        {
            AppFolders.MoveLegacyRecipeFolder(_base);

            Assert.Empty(Directory.GetFileSystemEntries(_base));
        }

        [Fact]
        public void GetRecipeFolder_IsNamedRecipes() =>
            Assert.Equal(AppFolders.RecipeFolderName, new DirectoryInfo(AppFolders.GetRecipeFolder()).Name);
    }
}
