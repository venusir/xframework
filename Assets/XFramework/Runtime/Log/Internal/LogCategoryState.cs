using System.Threading;

namespace XFramework.XLog.Internal
{
    /// <summary>
    /// 分类的可变状态：名字 + 档位覆盖。
    /// <para><b>为什么独立成对象</b>：<see cref="LogCategory"/> 是 readonly struct，复制出去的句柄必须与注册表
    /// 看到同一份档位——档位存在这里（引用类型），<c>SetCategoryLevel</c> 才能对已复制出去的句柄立即生效，
    /// 不会出现「副本还拿着旧档位」。也因此值比较用引用相等（同名分类全局同一个状态对象）。</para>
    /// </summary>
    internal sealed class LogCategoryState
    {
        /// <summary>档位覆盖的哨兵：跟随全局档。用哨兵而不是「注册时快照全局档」，改全局档才能影响到旧分类。</summary>
        internal const int FollowGlobal = -1;

        private int _levelOverride = FollowGlobal;

        internal LogCategoryState(string name)
        {
            Name = name;
        }

        /// <summary>分类名（不含方括号）。</summary>
        internal string Name { get; }

        /// <summary>档位覆盖；<see cref="FollowGlobal"/> 表示跟随全局档。</summary>
        internal int LevelOverride => Volatile.Read(ref _levelOverride);

        internal void SetLevelOverride(LogLevel level)
        {
            Volatile.Write(ref _levelOverride, (int)level);
        }

        internal void ResetLevelOverride()
        {
            Volatile.Write(ref _levelOverride, FollowGlobal);
        }
    }
}
