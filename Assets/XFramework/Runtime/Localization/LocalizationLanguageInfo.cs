namespace XFramework.XLocalization
{
    /// <summary>
    /// 一种已缓存语言的状态（诊断用只读快照）。
    /// <para><b>回答什么问题</b>：现在缓存里有哪些语言、各有多少条目、当前语言与回退语言分别是哪一条
    /// ——本地化「切了语言没生效」「键查不到」这类问题要先知道缓存里到底有什么。</para>
    /// <para><b>怎么取</b>：<see cref="LocalizationManager.CopyLoadedLanguages(System.Collections.Generic.List{LocalizationLanguageInfo})"/>，
    /// 按最近使用先后（MRU 在前）。</para>
    /// </summary>
    public readonly struct LocalizationLanguageInfo
    {
        /// <summary>语言代码。</summary>
        public readonly string Language;

        /// <summary>该语言的条目数。</summary>
        public readonly int EntryCount;

        /// <summary>是否是当前语言。</summary>
        public readonly bool IsCurrent;

        /// <summary>是否是回退语言。</summary>
        public readonly bool IsFallback;

        internal LocalizationLanguageInfo(string language, int entryCount, bool isCurrent, bool isFallback)
        {
            Language = language;
            EntryCount = entryCount;
            IsCurrent = isCurrent;
            IsFallback = isFallback;
        }

        /// <summary>单行摘要，形如 <c>zh-Hans(当前, 128 条)</c>。</summary>
        public override string ToString()
        {
            string tag = IsCurrent ? "(当前)" : (IsFallback ? "(回退)" : string.Empty);
            return $"{Language}{tag}{(tag.Length > 0 ? ", " : "(")}{EntryCount} 条)";
        }
    }
}
