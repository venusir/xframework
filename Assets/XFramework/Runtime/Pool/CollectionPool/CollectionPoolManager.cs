using System;
using System.Collections.Generic;

namespace XFramework.XPool
{
    /// <summary>
    /// 集合池统一管理器。
    /// <para>所有集合池（<see cref="ListPool{T}"/>、<see cref="HashSetPool{T}"/>、<see cref="DictionaryPool{TKey, TValue}"/>、
    /// <see cref="StringBuilderPool"/>）在静态构造时自动注册 <c>Clear</c> 回调。</para>
    /// <para><c>ClearAll()</c> 可一键清空所有已被触碰过的集合池，适用于切场景 / 退出游戏。</para>
    /// </summary>
    /// <remarks>
    /// <b>工作原理：</b>
    /// <para>每个集合池在静态构造函数中调用 <c>CollectionPoolManager.Register(clearAction)</c>。</para>
    /// <para>泛型池（如 <c>ListPool&lt;EnemyData&gt;</c>）只在首次使用时才触发静态构造并注册，未被触碰的类型不会被清空。</para>
    /// </remarks>
    public static class CollectionPoolManager
    {
        private static readonly List<Action> _clearActions = new();

        /// <summary>
        /// 会话复位回调：把各集合池的内部 <c>Pool</c> 重建为默认配置。
        /// <para>与 <c>_clearActions</c> 一样只在静态构造时注册；静态构造在关闭域重载时不重跑，
        /// 故清空这张表等于永久失去复位能力。</para>
        /// </summary>
        private static readonly List<Action> _resetActions = new();

        /// <summary>
        /// 已注册的 <c>Clear</c> 回调总数。
        /// <para>仅供单元测试观察注册行为（internal），业务代码不应依赖。</para>
        /// </summary>
        internal static int RegisteredActionCount => _clearActions.Count;

        /// <summary>
        /// 注册一个集合池的 <c>Clear</c> 回调。由各集合池的静态构造函数调用，不应手动使用。
        /// </summary>
        /// <param name="clearAction">池的 Clear 回调</param>
        public static void Register(Action clearAction)
        {
            if (clearAction == null) return;
            _clearActions.Add(clearAction);
        }

        /// <summary>
        /// 清空所有已注册集合池的闲置实例。
        /// <para>仅清空已被触碰过的集合池（即至少调用过一次 <c>Get()</c> 的类型）。</para>
        /// <para>已在外部使用的活跃实例不受影响，但归还时会重新入池。</para>
        /// </summary>
        public static void ClearAll()
        {
            foreach (var clearAction in _clearActions)
            {
                clearAction();
            }
        }

        /// <summary>
        /// 注册一个集合池的「重建内部池」回调。由各集合池的静态构造函数调用（internal：不是使用方入口）。
        /// </summary>
        /// <param name="resetAction">把内部 <c>Pool</c> 重建为默认配置的回调。</param>
        internal static void RegisterReset(Action resetAction)
        {
            if (resetAction == null) return;
            _resetActions.Add(resetAction);
        }

        /// <summary>
        /// 会话边界：把所有已被触碰过的集合池重建为默认配置。
        /// <para>只重建内部池、不 Dispose 任何已取出的实例——与其余门面的「只丢引用不 Dispose」同口径；
        /// 上一会话未归还的实例会在归还时落到旧池（与 <c>PooledObject.Dispose</c> 的既有语义一致）。</para>
        /// </summary>
        internal static void ResetForNewSession()
        {
            foreach (var resetAction in _resetActions)
            {
                resetAction();
            }
        }
    }
}