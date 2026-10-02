using System;

namespace XFramework.XPool
{
    /// <summary>
    /// 一个对象池的规模快照（诊断用）。
    /// <para><b>回答什么问题</b>：哪些池被建起来了、各自攒了多少闲置、有没有活跃实例长期不归还
    /// ——「池在漏」的典型形状是 <see cref="CountActive"/> 只增不减。</para>
    /// <para><b>怎么取</b>：<see cref="PoolManager.CopyPoolStats(System.Collections.Generic.List{PoolStats})"/>。
    /// 只反映**已创建**的池——池是首次 <c>Get&lt;T&gt;()</c> 时惰性建的，没建过的类型不在这张表里。</para>
    /// </summary>
    public readonly struct PoolStats
    {
        /// <summary>池化的对象类型。</summary>
        public readonly Type ObjectType;

        /// <summary>闲置实例数（栈里等着被取走的那批）。</summary>
        public readonly int CountInactive;

        /// <summary>活跃实例数（已取出、未归还）。</summary>
        public readonly int CountActive;

        /// <summary>累计创建过的实例数（只增不减，除非池被清空）。</summary>
        public readonly int CountAll;

        internal PoolStats(Type objectType, int countInactive, int countActive, int countAll)
        {
            ObjectType = objectType;
            CountInactive = countInactive;
            CountActive = countActive;
            CountAll = countAll;
        }

        /// <summary>单行摘要，形如 <c>MyData(闲置 3, 活跃 1, 累计 4)</c>。</summary>
        public override string ToString()
            => $"{ObjectType?.Name}(闲置 {CountInactive}, 活跃 {CountActive}, 累计 {CountAll})";
    }
}
