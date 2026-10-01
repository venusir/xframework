using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 加载缝的测试替身：不依赖 YooAsset，可控制加载时序、失败与取消，并<b>记录租约的发放与归还次数</b>。
    /// <para><b>为什么必须能观测归还</b>：本模块最要紧的不变量是「资源租约的释放点恰好一次」——
    /// 少一次即资源泄漏，多一次即重复释放。这条只能靠替身记账来断言。</para>
    /// <para><b>默认同步完成</b>：<see cref="DelayFrames"/> 为 <c>0</c> 时本方法不含任何挂起点，因此
    /// <c>AudioManager.Play</c> 会同步走完整个加载并起播——用例不必等帧就能断言播放状态。</para>
    /// </summary>
    internal sealed class FakeAudioClipLoader : IAudioClipLoader
    {
        #region Private Fields

        private readonly List<string> _requested = new List<string>();

        private int _issued;
        private int _released;

        #endregion

        #region Internal

        /// <summary>成功加载时返回的本体；为 <c>null</c> 表示加载失败（返回无效租约）。</summary>
        internal AudioClip Clip;

        /// <summary>加载前等待的帧数，用来构造「仍在加载中」的窗口。</summary>
        internal int DelayFrames;

        /// <summary>非 <c>null</c> 时加载抛该异常（用于验证「缝的契约是失败不抛，真抛了要兜住」）。</summary>
        internal Exception ThrowOnLoad;

        /// <summary>被请求过的地址，按顺序。</summary>
        internal IReadOnlyList<string> Requested => _requested;

        /// <summary>调用过 <see cref="LoadAsync"/> 的次数。</summary>
        internal int LoadCount { get; private set; }

        /// <summary>发放过的有效租约数。</summary>
        internal int IssuedLeases => _issued;

        /// <summary>被归还的租约数。</summary>
        internal int ReleasedLeases => _released;

        /// <summary>已发放但尚未归还的租约数。<b>离开用例时必须为 <c>0</c></b>，否则就是泄漏。</summary>
        internal int OutstandingLeases => _issued - _released;

        #endregion

        #region IAudioClipLoader

        /// <inheritdoc/>
        public async UniTask<AudioClipLease> LoadAsync(string location, CancellationToken cancellationToken)
        {
            _requested.Add(location);
            LoadCount++;

            if (DelayFrames > 0)
                await UniTask.DelayFrame(DelayFrames, cancellationToken: cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (ThrowOnLoad != null)
                throw ThrowOnLoad;

            if (Clip == null)
                return default;

            _issued++;
            return new AudioClipLease(Clip, OnLeaseReleased);
        }

        #endregion

        #region Private

        private void OnLeaseReleased() => _released++;

        #endregion
    }
}
