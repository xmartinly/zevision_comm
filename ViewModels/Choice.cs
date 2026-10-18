namespace Zevision.ViewModels
{
    /// <summary>下拉框中的一项:值、显示文本和鼠标提示。</summary>
    /// <typeparam name="T">值的类型。</typeparam>
    public sealed class Choice<T> : ImmutableBindingSource
    {
        /// <summary>创建选项。</summary>
        public Choice(T value, string text, string description)
        {
            Value = value;
            Text = text;
            Description = description;
        }

        /// <summary>值。</summary>
        public T Value { get; }

        /// <summary>显示文本。</summary>
        public string Text { get; }

        /// <summary>鼠标提示。</summary>
        public string Description { get; }
    }
}
