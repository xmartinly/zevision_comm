namespace Zevision.Core.Configuration
{
    /// <summary>
    /// 仪器配置文件 (recipe) 的读写。文件格式、MD5 校验和安全覆盖由实现负责 (数据存储层)。
    /// </summary>
    public interface IRecipeStore
    {
        /// <summary>配置文件的默认目录 (文件对话框的初始目录)。</summary>
        string Folder { get; }

        /// <summary>配置文件的扩展名,如 <c>.json</c>。</summary>
        string FileExtension { get; }

        /// <summary>
        /// 计算并写入 <see cref="InstrumentRecipe.Md5"/> 后保存到文件 (覆盖)。失败时原因可直接显示。
        /// </summary>
        Result Save(InstrumentRecipe recipe, string path);

        /// <summary>
        /// 读取并校验文件 (格式版本、MD5、字段、ID)。不合格时返回失败,原因可直接显示。
        /// </summary>
        Result<InstrumentRecipe> Load(string path);
    }
}
