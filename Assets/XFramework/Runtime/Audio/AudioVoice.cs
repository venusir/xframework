using UnityEngine;

namespace XFramework.XAudio
{
    /// <summary>
    /// 一个播放源槽位的状态。
    /// <para><b>为什么要自持状态机而不是只看 <c>AudioSource.isPlaying</c></b>：实测（见
    /// <c>Documentation/Modules/Audio.md</c> 的实测记录）表明 <c>Pause()</c> 之后 <c>isPlaying</c>
    /// 会变成 <c>false</c>——若回收判据只看它，暂停中的播放会被当场回收掉。状态机是唯一可信的来源。</para>
    /// </summary>
    internal enum AudioVoiceState
    {
        /// <summary>空闲，可被预占。</summary>
        Free = 0,

        /// <summary>已被预占，资源加载中。<b>绝不被回收</b>——加载续体负责收尾。</summary>
        Loading,

        /// <summary>已经调过 <c>Play()</c>，但还没观察到 <c>isPlaying</c> 变真。</summary>
        Starting,

        /// <summary>正在播放。</summary>
        Playing,

        /// <summary>
        /// 被 <c>Pause()</c> 暂停。
        /// <para><b>本状态的存在理由就是回收判据</b>：实测确认 <c>Pause()</c> 之后 <c>isPlaying</c> 会变成
        /// <c>false</c>，只看它会把暂停中的播放当场回收掉。有了这个状态，回收逻辑才有一处可信的依据。</para>
        /// </summary>
        Paused,
    }

    /// <summary>
    /// 播放源池里一个槽位的记录。
    /// <para>字段全为 <c>internal</c>：这是模块内部的可变记账，不构成任何契约。</para>
    /// <para><b>创建时就把 <see cref="Source"/> 缓存下来</b>，运行期不再有任何 <c>GetComponent</c>。</para>
    /// </summary>
    internal sealed class AudioVoice
    {
        /// <summary>该槽位独占的播放源。随槽位 GameObject 一同创建，之后不更换。</summary>
        internal AudioSource Source;

        /// <summary>
        /// 代际号。每次被预占时递增（跳过 <c>0</c>），使上一个持有者的句柄失效。
        /// <para>这是「槽位复用后旧句柄不会误停新声音」的唯一保证。</para>
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

        /// <summary>归一化后的通道名。仅用于 <c>StopAll(channel)</c> 的过滤与 C3 起的音量归属。</summary>
        internal string Channel;

        /// <summary>本次播放的资源地址（诊断用）。</summary>
        internal string Location;

        /// <summary>是否循环。<b>循环的播放永不被自动回收</b>，必须显式停止。</summary>
        internal bool Loop;

        /// <summary>
        /// 本次播放的音量缩放（已归一化，<c>&gt;= 1</c> 或原值）。
        /// <para>缓存下来是为了让音量变更能重算正在播的声音——实际音量是
        /// 「主音量 × 通道音量 × 本值」，三者任一变化都要重写 <c>AudioSource.volume</c>。</para>
        /// </summary>
        internal float VolumeScale = 1f;

        /// <summary>处于 <see cref="AudioVoiceState.Starting"/> 期间累计的扫描次数，用于超时兜底。</summary>
        internal int StartingTicks;
    }
}
