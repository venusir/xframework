using System;
using System.Collections.Generic;

namespace XFramework.XReactive
{
    /// <summary>
    /// 响应式字典接口：只读视图 + 变更流。
    /// <para><b>接口是只读视图</b>——增删改经具体实现类型（<see cref="ReactiveDictionary{TKey, TValue}"/>），
    /// 与 <see cref="IReactiveProperty{T}"/> / <see cref="IReactiveList{T}"/> 同旨：第三方拿到的只能是读面。</para>
    /// <para><b>订阅不做立即回调</b>——与同模块的 <see cref="IReactiveProperty{T}.Subscribe"/> 相反：
    /// 那是「值」的订阅（契约是订阅时立即同步回调当前值），这是<b>变更流</b>的订阅（语义对齐
    /// <c>XEvent.IEventStream&lt;T&gt;.Subscribe</c>，不重放、也无法重放）。
    /// 初始化配方：<b>订阅后立即读一次字典</b>——主线程同步，两步之间不可能发生变更。</para>
    /// <para><b>遍历顺序不作承诺</b>：不保证插入序，也不承诺底层 <c>Dictionary</c> 当前的实现序。</para>
    /// <para>线程：与 Reactive 模块其余类型一致，<b>主线程专用</b>。</para>
    /// <para>派发顺序 LIFO（后订阅先收到，引擎语义）；<b>回调里不要改动本字典</b>，后果见
    /// <see cref="ReactiveDictionary{TKey, TValue}"/> 的说明。</para>
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    public interface IReactiveDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    {
        /// <summary>
        /// 订阅变更。<b>不做立即回调</b>（与 <see cref="IReactiveProperty{T}.Subscribe"/> 相反）：
        /// 当前状态直接读字典本身。
        /// <para>返回的 <see cref="IDisposable"/> 释放后不再收到投递（幂等）；退订在同一轮派发内
        /// 立即生效；回调抛出的异常按引擎约定记 Error 日志后继续，不影响其余订阅者。</para>
        /// </summary>
        /// <param name="handler">变更回调，不可为 null。</param>
        /// <returns>订阅句柄；释放它即不再收到投递。</returns>
        IDisposable Subscribe(Action<ReactiveDictionaryChange<TKey, TValue>> handler);
    }
}
