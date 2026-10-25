using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XAudio
{
    /// <summary>
    /// 播放引擎缝。核心 <see cref="AudioManagerImpl"/> 保留通道、三档音量、暂停闸门、计数、
    /// 参数归一化与诊断；本接口只负责「实际播放」这一层。
    /// <para><b>推荐第三方中间件实现本接口</b>，而不是整体实现 <see cref="IAudioManager"/>：
    /// 前者不必重写框架语义，后者是整体替换管理器的逃生口。</para>
    /// <para><b>线程</b>：全部成员限主线程调用。</para>
    /// <para><b><see cref="IDisposable.Dispose"/></b>：幂等；必须取消在途 <see cref="PlayAsync"/>、
    /// 释放全部实例与租约。核心在管理器销毁时调用它并隔离异常。</para>
    /// </summary>
    public interface IAudioProvider : IDisposable
    {
        /// <summary>
        /// 起播一个 location 并返回 provider 句柄。
        /// <para><b>失败返回 <c>default(AudioProviderHandle)</c> 并自行记日志，不抛</b>；
        /// <paramref name="cancellationToken"/> 取消时抛 <see cref="OperationCanceledException"/>，
        /// 且不得泄漏已加载资源或实例。</para>
        /// <para><b>两个取消来源要分开</b>：<paramref name="cancellationToken"/> 是调用方/生命周期取消；
        /// <see cref="AudioPlaybackRequest.ValidityToken"/> 是核心槽位有效性令牌。后者在
        /// <c>Stop</c>/<c>StopAll</c> 作废槽位时取消；实现必须在 <c>start()</c> 之前检查它，
        /// 已取消则释放并返回 default（不要抛 <see cref="OperationCanceledException"/>）。
        /// <b>validity 取消时必须立即归还实现自己持有的预占槽位，不能等加载完成</b>——否则核心
        /// <c>Stop</c> 后立即重播会撞上 provider 的容量窗口，拿到必然失效的预占句柄。</para>
        /// <para><see cref="AudioPlaybackRequest.Volume"/> 是核心算好的最终音量，
        /// 实现<b>不得</b>再乘主音量 / 通道音量 / <c>VolumeScale</c>。</para>
        /// </summary>
        /// <param name="location">资源地址；中间件实现可解释成自己的事件名。</param>
        /// <param name="request">本次播放的最终参数。</param>
        /// <param name="cancellationToken">调用方/生命周期取消令牌。</param>
        /// <returns>provider 句柄；失败返回 default。</returns>
        UniTask<AudioProviderHandle> PlayAsync(
            string location,
            AudioPlaybackRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止一次播放。<b>只停播，不释放</b>；释放由 <see cref="Release"/> 负责。
        /// 对 default / 过期 / 未知句柄安全返回 <c>false</c>，不抛。
        /// </summary>
        /// <param name="handle">provider 句柄。</param>
        /// <returns>真的停掉了一次当前播放时返回 <c>true</c>。</returns>
        bool Stop(AudioProviderHandle handle);

        /// <summary>
        /// 释放一次播放的资源与槽位。<b>幂等</b>；核心在用户 <c>Stop</c> 后与自然结束时都会调用。
        /// 对 default / 过期 / 未知句柄是空操作。
        /// </summary>
        /// <param name="handle">provider 句柄。</param>
        void Release(AudioProviderHandle handle);

        /// <summary>
        /// 暂停或恢复一次播放。返回 <c>false</c> 表示该句柄已失效（播放源被外部销毁等），
        /// 核心据此回收槽位。
        /// </summary>
        /// <param name="handle">provider 句柄。</param>
        /// <param name="paused">是否暂停。</param>
        bool SetPaused(AudioProviderHandle handle, bool paused);

        /// <summary>
        /// 写入最终音量（线性，已由核心钳制）。返回 <c>false</c> 表示句柄已失效。
        /// </summary>
        /// <param name="handle">provider 句柄。</param>
        /// <param name="volume">最终音量。</param>
        bool SetVolume(AudioProviderHandle handle, float volume);

        /// <summary>
        /// 查询播放状态。<b>纯查询</b>：不推进超时、不修改状态。
        /// <para>由核心 ticker 每拍调用：<c>Starting</c> / <c>Playing</c> 在管理器未暂停时 poll，
        /// <c>Paused</c> 槽位始终 poll（回收外部销毁的播放源）；<c>Loading</c> 还没有 provider 句柄，不 poll。</para>
        /// </summary>
        /// <param name="handle">provider 句柄。</param>
        /// <returns>当前播放状态。</returns>
        AudioProviderPlaybackState Poll(AudioProviderHandle handle);
    }
}
