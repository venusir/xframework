using System.Threading;

namespace XFramework.XAudio
{
    /// <summary>
    /// 核心语义层的一个槽位状态。
    /// <para><b>与 provider 的槽位状态分开</b>：provider 只管「实际播放」，核心保留
    /// Loading/Starting/Playing/Paused 与起播超时。</para>
    /// </summary>
    internal enum AudioManagedVoiceState
    {
        /// <summary>空闲。</summary>
        Free = 0,

        /// <summary>已预占，provider 正在加载/创建。</summary>
        Loading,

        /// <summary>provider 已返回句柄，尚未观察到真正在播。</summary>
        Starting,

        /// <summary>正在播放。</summary>
        Playing,

        /// <summary>管理器级暂停。</summary>
        Paused,
    }

    /// <summary>
    /// 核心语义层的槽位记录。字段全部 internal，不构成契约。
    /// </summary>
    internal sealed class AudioManagedVoice
    {
        /// <summary>核心句柄的代际号；每次预占递增并跳过 0。</summary>
        internal int Generation;

        /// <summary>当前状态。</summary>
        internal AudioManagedVoiceState State;

        /// <summary>provider 句柄；Loading 期间为 default。</summary>
        internal AudioProviderHandle ProviderHandle;

        /// <summary>归一化后的通道名。</summary>
        internal string Channel;

        /// <summary>资源地址（诊断用）。</summary>
        internal string Location;

        /// <summary>单次播放的音量缩放（已归一化）。</summary>
        internal float VolumeScale = 1f;

        /// <summary><see cref="AudioManagedVoiceState.Starting"/> 期间的累计扫描次数。</summary>
        internal int StartingTicks;

        /// <summary>核心槽位有效性令牌源；Stop/StopAll 取消它，provider 在起播前检查。</summary>
        internal CancellationTokenSource ValidityCts;
    }
}
