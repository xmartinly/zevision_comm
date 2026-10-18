using Zevision.Core;
using Zevision.Core.Configuration;

namespace Zevision.Storage
{
    /// <summary>
    /// <see cref="IRecipeStore"/> 的实现:文件格式见 <see cref="RecipeFile"/>,默认目录见 <see cref="AppFolders.GetRecipeFolder"/>。
    /// </summary>
    public sealed class RecipeFileStore : IRecipeStore
    {
        /// <inheritdoc />
        public string Folder => _folder ??= AppFolders.GetRecipeFolder(); // 判断程序目录是否可写要创建临时文件,只做一次

        private string? _folder;

        /// <inheritdoc />
        public string FileExtension => RecipeFile.Extension;

        /// <inheritdoc />
        public Result Save(InstrumentRecipe recipe, string path) => RecipeFile.Save(recipe, path);

        /// <inheritdoc />
        public Result<InstrumentRecipe> Load(string path) => RecipeFile.Load(path);
    }
}
