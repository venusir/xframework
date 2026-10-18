using System;
using UnityEngine;
using XFramework.XAsset;

namespace XFramework.XAudio
{
    /// <summary>
    /// 一次音频资源的租约：资源本体 + 「归还时该释放什么」。
    /// <para><b>为什么不让加载缝直接返回 <c>AssetHandle&lt;AudioClip&gt;</c></b>：那会把本模块的池、voice、
    /// 编排三处全部绑在 <c>XAsset</c> 的句柄类型上；更要紧的是，<c>AssetHandle</c> 内部包着 YooAsset 的句柄，
    /// <b>没有活的 YooAsset 环境就造不出有效实例</b>，于是加载缝的实现方（尤其是测试替身）根本无法表达
    /// 「加载成功」，更无法观测「释放了几次」——而「不 Dispose 即泄漏」正是本模块最要紧的不变量。</para>
    /// <para><b>契约</b>：<see cref="IsValid"/> 只表示「资源本体是否拿到」；<see cref="Dispose"/> 无条件
    /// 执行释放动作（<c>default</c> 与没有释放动作的租约是安全空操作）。<b>不得按 IsValid 跳过 Dispose</b>：
    /// location 指向非 AudioClip 时加载成功但本体为 null，租约仍持有底层句柄的释放动作（AUD-004）。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>代价（如实记录）</b>：释放动作以委托形式持有，因此每次成功加载会产生一次委托分配。
    /// 播放不是每帧路径（<c>Play</c> 由使用方按需调用，一次音效一次），相对其内部的 UniTask 状态机与
    /// 资源加载开销可以忽略；换成「按类型分支存句柄」能省掉它，但会把替身实现重新变成不可能。
    /// 这条取舍以「可测」优先。</para>
    /// </remarks>
    internal readonly struct AudioClipLease : IDisposable
    {
        #region Private Fields

        private readonly AudioClip _clip;
        private readonly Action _release;

        #endregion

        #region Constructors

        /// <summary>
        /// 从一个 <c>XAsset</c> 句柄构造（默认加载缝走这条）。
        /// <para>资源本体在构造时读取一次并缓存——<c>Asset</c> 每次都经 YooAsset 取值，没有理由反复取。</para>
        /// </summary>
        internal AudioClipLease(AssetHandle<AudioClip> handle)
        {
            _clip = handle.IsValid ? handle.Asset : null;
            _release = handle.Dispose;
        }

        /// <summary>
        /// 从资源本体与释放动作构造。供加载缝的实现方在「资源已经在我手上」时使用。
        /// </summary>
        /// <param name="clip">资源本体；为 <c>null</c> 表示加载失败。</param>
        /// <param name="release">归还时要执行的动作；可为 <c>null</c>（表示没有需要释放的东西）。</param>
        internal AudioClipLease(AudioClip clip, Action release)
        {
            _clip = clip;
            _release = release;
        }

        #endregion

        #region Internal

        /// <summary>资源本体；加载失败为 <c>null</c>。</summary>
        internal AudioClip Clip => _clip;

        /// <summary>租约是否有效（即资源本体是否拿到了）。</summary>
        internal bool IsValid => _clip != null;

        #endregion

        #region IDisposable

        /// <summary>
        /// 归还租约，执行它的释放动作——那是底层资源唯一的释放凭据，不调用即泄漏。
        /// <para><c>default(AudioClipLease)</c> 与无释放动作的租约都是安全的空操作；<b>与
        /// <see cref="IsValid"/> 无关</b>（见类型注释，AUD-004）。</para>
        /// </summary>
        public void Dispose() => _release?.Invoke();

        #endregion
    }
}
