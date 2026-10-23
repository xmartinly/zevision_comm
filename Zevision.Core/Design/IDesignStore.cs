using System.Collections.Generic;

namespace Zevision.Core.Design
{
    /// <summary>
    /// 工艺编辑的存储:材料和工艺 (程序可读写的文件,按材料、膜系、膜层、工艺分目录) 和材料库 (程序旁边的只读文件,选择材料时填入名称、密度、Z 比、中文名)。
    /// </summary>
    public interface IDesignStore
    {
        /// <summary>材料和工艺的保存位置 (目录,显示给用户)。</summary>
        string Path { get; }

        /// <summary>读取材料和工艺;还没有保存过时为空的内容。有文件损坏或格式版本不受支持时失败,原因可直接显示。</summary>
        Result<DesignDocument> Load();

        /// <summary>保存全部材料和工艺 (每个文件先写临时文件再替换;删除的对象的文件随之删除)。失败时原因可直接显示。</summary>
        Result Save(DesignDocument document);

        /// <summary>读取材料库;文件不存在时为空。文件损坏时失败,原因可直接显示。</summary>
        Result<IReadOnlyList<MaterialLibraryEntry>> LoadMaterialLibrary();
    }
}
