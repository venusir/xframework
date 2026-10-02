namespace XFramework.XLog
{
    /// <summary>
    /// 一个日志分类的档位状态（诊断用只读快照）。
    /// <para><b>回答什么问题</b>：这条分类现在到底会不会输出、「为什么它不输出」的答案落在哪一层
    /// ——是分类自己设了覆盖（<see cref="IsOverridden"/> 为 true），还是在跟随全局档。</para>
    /// <para><b>怎么取</b>：<see cref="LogManager.CopyCategories(System.Collections.Generic.List{LogCategoryInfo})"/>。
    /// 只对内置实现成立：注入第三方 <see cref="ILogManager"/> 时回读为空（那类实现不提供分类表）。</para>
    /// </summary>
    public readonly struct LogCategoryInfo
    {
        /// <summary>分类名（不含方括号）。</summary>
        public readonly string Name;

        /// <summary>当前实际生效的档位：设了覆盖就是覆盖值，否则是全局档。</summary>
        public readonly LogLevel EffectiveLevel;

        /// <summary>是否被显式设过档位覆盖（false = 跟随全局档，改全局档会影响它）。</summary>
        public readonly bool IsOverridden;

        internal LogCategoryInfo(string name, LogLevel effectiveLevel, bool isOverridden)
        {
            Name = name;
            EffectiveLevel = effectiveLevel;
            IsOverridden = isOverridden;
        }

        /// <summary>单行摘要，形如 <c>Config: Info（覆盖）</c>。</summary>
        public override string ToString()
            => IsOverridden ? $"{Name}: {EffectiveLevel}（覆盖）" : $"{Name}: {EffectiveLevel}";
    }
}
