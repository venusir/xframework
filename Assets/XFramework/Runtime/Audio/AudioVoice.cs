using UnityEngine;

namespace XFramework.XAudio
{
    /// <summary>
    /// 内置播放引擎（<see cref="AudioSourceProvider"/>）的一个槽位状态。
    /// <para><b>为什么要自持状态机而不是只看 <c>AudioSource.isPlaying</c></b>：实测表明 <c>Pause()</c>
    /// 之后 <c>isPlaying</c> 会变成 <c>false</c>——若回收判据只看它，暂停中的播放会被当场回收掉。
    /// 状态机是唯一可信的来源。</para>
    /// <para>核心语义层的状态在 <see cref="AudioManagedVoiceState"/>；两者不共用同一个枚举。</para>
    /// </summary>
    internal enum AudioVoiceState
    {
        /// <summary>空闲，可被预占。</summary>
        Free = 0,

        /// <summary>已被预占，资源加载中。</summary>
        Loading,

        /// <summary>已经调过 <c>Play()</c>，但还没观察到 <c>isPlaying</c> 变真。</summary>
        Starting,

        /// <summary>正在播放。</summary>
        Playing,

        /// <summary>
        /// 被暂停。
        /// <para><b>本状态的存在理由就是回收判据</b>：实测确认 <c>Pause()</c> 之后 <c>isPlaying</c> 会变成
        /// <c>false</c>，只看它会把暂停中的播放当场回收掉。</para>
        /// </summary>
        Paused,
    }

    /// <summary>
    /// 内置播放引擎的一个槽位记录。字段全部 internal，不构成契约。
    /// </summary>
    internal sealed class AudioVoice
    {
        /// <summary>该槽位独占的播放源。随槽位 GameObject 一同创建，之后不更换。</summary>
        internal AudioSource Source;

        /// <summary>
        /// 承载 <see cref="Source"/> 的槽位 GameObject。
        /// <para>外部销毁宿主或播放源后，<see cref="Source"/> 会变成 Unity 伪 null；保留这个引用让
        /// <c>TryReserve</c> 能在原下标重建（R4-AUD-001）。它同样可能变成伪 null（宿主整体销毁）。</para>
        /// </summary>
        internal GameObject SlotObject;

        /// <summary>
        /// 代际号。每次被预占时递增（跳过 <c>0</c>），使上一个持有者的 provider 句柄失效。
        /// </summary>
        internal int Generation;

        /// <summary>当前状态。</summary>
        internal AudioVoiceState State;

        /// <summary>
        /// 本次播放持有的资源租约。
        /// <para>它既是<b>释放凭据</b>（不 Dispose 即泄漏）也是<b>保活凭据</b>（持有期间底层资源不会被
        /// <c>UnloadUnusedAssetsAsync</c> 回收）。<b>释放点必须恰好一次</b>。</para>
        /// </summary>
        internal AudioClipLease ClipLease;

        /// <summary>本次播放的资源地址（诊断用）。</summary>
        internal string Location;

        /// <summary>是否循环。<b>循环的播放永不被自动回收</b>，必须显式停止。</summary>
        internal bool Loop;
    }
}
