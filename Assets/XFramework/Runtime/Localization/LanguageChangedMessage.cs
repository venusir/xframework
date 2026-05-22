namespace XFramework.XLocalization
{
    /// <summary>
    /// 语言切换消息。由 <see cref="LocalizationManagerImpl"/> 在切换语言时通过 <see cref="XReactive.MessageManager.Publish"/> 发送。
    /// <para>任意模块可通过 <c>MessageManager.Subscribe<LanguageChangedMessage>()</c> 监听。</para>
    /// <para>使用 <see langword="readonly struct"/> 避免 GC 分配。</para>
    /// </summary>
    public readonly struct LanguageChangedMessage
    {
        /// <summary>
        /// 新语言标识，如 <c>"zh_Hans"</c>, <c>"en"</c>。
        /// </summary>
        public readonly string Language;

        public LanguageChangedMessage(string language)
        {
            Language = language;
        }
    }
}