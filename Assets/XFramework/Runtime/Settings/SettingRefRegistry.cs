using System;
using System.Collections.Generic;
using XFramework.XLog;

namespace XFramework.XSettings
{
    /// <summary>
    /// 可重放的字段句柄契约：把「设置实例被换掉后，请把当前值重放给你的订阅者」这一能力
    /// 从泛型的字段类型中剥离，使注册表无需知道 <c>TField</c> 即可遍历同一设置类型下的全部句柄。
    /// <para>由 <see cref="SettingRef{T,TField}"/> <b>显式</b>实现，故不出现在其公开面上。</para>
    /// </summary>
    /// <typeparam name="T">设置对象类型。</typeparam>
    internal interface IReplayableSettingRef<T> where T : class, new()
    {
        /// <summary>把 <paramref name="settings"/> 中对应字段的当前值推送给订阅者。</summary>
        /// <param name="settings">刚就位的设置实例。</param>
        void Replay(T settings);
    }

    /// <summary>
    /// 按设置类型登记的字段句柄注册表，供 <see cref="SettingsManagerImpl{T}"/> 在实例被整体替换时重放。
    /// <para><b>为什么需要它：</b><see cref="SettingRef{T,TField}"/> 每次读写都重新解析当前实例，
    /// 所以「值」永远是对的；但订阅者只在经句柄写入时才收到推送。于是
    /// <c>Load</c> / <c>Reset</c> / <c>Apply</c> 换掉实例之后，UI 绑定会停留在旧值——
    /// 玩家点「恢复默认」而滑条不动，正是这条缺口。本注册表把「实例替换」也变成一次推送。</para>
    /// <para><b>为什么是泛型静态类而不是 <c>Dictionary&lt;Type, List&lt;…&gt;&gt;</c>：</b>
    /// 封闭泛型本身就是类型索引——无需字典、无需 <c>object</c> 装拆箱、无需与
    /// <see cref="SettingsManager"/> 的注册表同步生命周期。句柄创建处
    /// （<see cref="SettingRef{T,TField}.Create"/>）与重放处（管理器）用同一个
    /// <typeparamref name="T"/> 实例化，必然命中同一个静态。</para>
    /// <para><b>刻意不在 <see cref="SettingsManager.Destroy"/> 里清空：</b>理由与其门面的
    /// <c>DefaultPathClaims</c> 同构——句柄是常驻对象（通常声明为 <c>static readonly</c>，活到进程结束），
    /// 若清空，则「创建句柄 → Destroy → 重新 Initialize」之后句柄读值仍然正确、却再也不重放，
    /// 正是本模块文档反复点名的「看起来能用、实际静默失效」。</para>
    /// <para><b>线程：</b>与门面的管理器注册表一致，只允许主线程创建与重放。</para>
    /// </summary>
    internal static class SettingRefRegistry<T> where T : class, new()
    {
        #region Private Fields

        /// <summary>本设置类型下已创建的句柄。创建即入表，无退表路径（见类型注释）。</summary>
        private static readonly List<IReplayableSettingRef<T>> Refs = new();

        #endregion

        #region Internal

        /// <summary>
        /// 登记一个句柄。由 <see cref="SettingRef{T,TField}.Create"/> 调用。
        /// </summary>
        /// <param name="handle">新创建的句柄。</param>
        internal static void Register(IReplayableSettingRef<T> handle)
        {
            Refs.Add(handle);
        }

        /// <summary>
        /// 把 <paramref name="settings"/> 的当前值重放给本类型下所有句柄的订阅者。
        /// <para><b>无条件重放：</b>不做「新旧值是否相同」的比较。可比较的只有「上一实例的字段值」，
        /// 而需要知道的是「订阅者上次收到什么」，二者恰在本模块已文档化的「直改 POCO 不通知」
        /// 缺口上分叉——直改成 1f 再 <c>Load</c> 回 1f，比较会判定「没变」，让订阅者永久停留在
        /// 陈旧值，正是本次要消灭的那类缺陷。故取无条件：宁可多一次幂等回调，也不要静默陈旧。</para>
        /// </summary>
        /// <param name="settings">刚就位的设置实例。</param>
        internal static void Replay(T settings)
        {
            var refs = Refs;

            // 先取计数：重放回调里若又调 Ref 创建句柄，新句柄不该参与本轮重放
            var count = refs.Count;
            for (var i = 0; i < count; i++)
            {
                // 逐个隔离：句柄的取值走嵌套路径（如 s.Audio.MasterVolume），
                // 新实例的中间段为 null 时取值会抛。一个坏句柄不该让整次 Reset / Load / Apply 失败
                try
                {
                    refs[i].Replay(settings);
                }
                catch (Exception e)
                {
                    LogManager.Exception(LogCategories.SettingsManager, e,
                        string.Format("字段句柄重放失败（{0}）", refs[i]));
                }
            }
        }

        #endregion
    }
}
