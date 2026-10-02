using System;
using System.Collections.Generic;

namespace XFramework.XLog.Internal
{
    /// <summary>
    /// 分类驻留表：同名分类全局同一个状态对象。
    /// <para><b>读路径无锁</b>：字典按 copy-on-write 整体替换，读到的永远是某个完整快照——日志可能来自任意线程，
    /// 读路径不能加锁也不该加锁。<b>写路径串行</b>：注册发生在首次使用（含静态初始化），加锁的开销可忽略。</para>
    /// </summary>
    internal static class LogRegistry
    {
        #region Private Fields

        private static readonly object WriteLock = new object();

        /// <summary>volatile：无锁读必须看到最新的字典引用。</summary>
        private static volatile Dictionary<string, LogCategoryState> _map =
            new Dictionary<string, LogCategoryState>(StringComparer.Ordinal);

        #endregion

        #region Internal API

        /// <summary>已注册的分类数（含框架内置）。诊断用。</summary>
        internal static int Count => _map.Count;

        /// <summary>按名称取得（或创建）分类状态；名字为 null/空白时返回 null。</summary>
        internal static LogCategoryState GetOrCreate(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            Dictionary<string, LogCategoryState> map = _map;
            if (map.TryGetValue(name, out LogCategoryState existing))
                return existing;

            lock (WriteLock)
            {
                map = _map;
                if (map.TryGetValue(name, out existing))
                    return existing;

                var state = new LogCategoryState(name);
                var next = new Dictionary<string, LogCategoryState>(map, StringComparer.Ordinal)
                {
                    [name] = state,
                };
                _map = next; // 先写满再发布：读路径永远看到完整字典
                return state;
            }
        }

        /// <summary>
        /// 把所有分类的当前状态对象写入缓冲区（先清空），返回条数。诊断用。
        /// <para>先取一次字典引用：copy-on-write 下它就是一份完整快照，遍历期间写者只会替换引用，
        /// 不会改动我们手里这本字典——与读路径无锁的理由是同一条。</para>
        /// </summary>
        internal static int CopyTo(List<LogCategoryState> buffer)
        {
            buffer.Clear();

            Dictionary<string, LogCategoryState> map = _map;
            foreach (KeyValuePair<string, LogCategoryState> pair in map)
                buffer.Add(pair.Value);

            return buffer.Count;
        }

        /// <summary>把所有分类的档位覆盖还原为「跟随全局」。<c>Shutdown</c> 用它复位。</summary>
        internal static void ResetAllLevels()
        {
            foreach (KeyValuePair<string, LogCategoryState> pair in _map)
                pair.Value.ResetLevelOverride();
        }

        #endregion
    }
}
