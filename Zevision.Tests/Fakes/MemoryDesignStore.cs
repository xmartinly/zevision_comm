using System.Collections.Generic;
using Zevision.Core;
using Zevision.Core.Design;

namespace Zevision.Tests
{
    /// <summary>内存中的工艺编辑存储:读取给定的内容,记录每次保存时的内容 (序列化前的对象)。</summary>
    internal sealed class MemoryDesignStore : IDesignStore
    {
        public MemoryDesignStore(DesignDocument? document = null, IReadOnlyList<MaterialLibraryEntry>? library = null)
        {
            Document = document ?? new DesignDocument();
            Library = library ?? new List<MaterialLibraryEntry>();
        }

        public DesignDocument Document { get; }

        public IReadOnlyList<MaterialLibraryEntry> Library { get; }

        /// <summary>保存的次数。</summary>
        public int SaveCount { get; private set; }

        /// <summary>读取的结果;为 null 时成功返回 <see cref="Document"/>。</summary>
        public string? LoadError { get; set; }

        /// <summary>保存的结果;为 null 时成功。</summary>
        public string? SaveError { get; set; }

        public string Path => @"C:\Zevision\recipes";

        public Result<DesignDocument> Load() => LoadError == null ? Result<DesignDocument>.Ok(Document) : Result<DesignDocument>.Fail(LoadError);

        public Result Save(DesignDocument document)
        {
            if (SaveError != null)
            {
                return Result.Fail(SaveError);
            }

            SaveCount++;
            return Result.Ok();
        }

        public Result<IReadOnlyList<MaterialLibraryEntry>> LoadMaterialLibrary() => Result<IReadOnlyList<MaterialLibraryEntry>>.Ok(Library);
    }
}
