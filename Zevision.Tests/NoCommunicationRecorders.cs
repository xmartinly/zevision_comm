using System;
using Zevision.Core;

namespace Zevision.Tests
{
    /// <summary>不保存通讯数据的工厂:测试中不勾选"保存通讯数据",调用即说明测试有误。</summary>
    internal sealed class NoCommunicationRecorders : ICommunicationRecorderFactory
    {
        public Result<ICommunicationRecorder> Create() => throw new InvalidOperationException("Saving communication data is not expected in this test.");
    }
}
