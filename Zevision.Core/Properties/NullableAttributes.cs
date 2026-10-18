namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// 可空分析用的特性 (.NET Framework 4.7.2 没有;C# 9 编译器按名称识别,引用本程序集的项目也能用到)。
    /// 属性返回 <see cref="ReturnValue"/> 时,<see cref="Members"/> 中的成员不为 null,如 <c>Result.IsSuccess</c> 为 false 时 <c>Error</c> 有值。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    internal sealed class MemberNotNullWhenAttribute : Attribute
    {
        /// <summary>创建特性。</summary>
        public MemberNotNullWhenAttribute(bool returnValue, string member)
        {
            ReturnValue = returnValue;
            Members = new[] { member };
        }

        /// <summary>成员不为 null 时的返回值。</summary>
        public bool ReturnValue { get; }

        /// <summary>不为 null 的成员。</summary>
        public string[] Members { get; }
    }
}
