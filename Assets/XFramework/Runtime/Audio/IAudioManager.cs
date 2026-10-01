using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XAudio
{
    /// <summary>
    /// 音频管理器接口。由 <see cref="AudioManager"/> 门面持有，所有公开调用转发到这里。
    /// <para><b>本接口是后端替换点</b>：第三方若要把底层播放引擎整体换成 Wwise / FMOD 等音频中间件，
    /// 实现本接口后经 <see cref="AudioManager.SetInstance"/> 注入即可——形态与 <c>XInput</c> 换 Rewired 一致
    /// （整套底层引擎被替换，而非按平台/能力切分的分层 Provider）。</para>
    /// <para><b>接口是后端中立的</b>：不出现任何 Unity 内置音频类型（没有 <c>AudioClip</c>、没有
    /// <c>AudioMixerGroup</c>、没有 <c>AudioSource.priority</c>）。资源一律以 <b>location 字符串</b>标识，
    /// 中间件实现可以直接把它解释成自己的事件名。</para>
    /// <para><b>句柄由实现自定编码</b>：<see cref="AudioHandle.Id"/> 与 <see cref="AudioHandle.Generation"/>
    /// 对门面不透明，实现自行产生与校验。框架级的唯一契约是 <c>default(AudioHandle)</c> 必须被视为无效。</para>
    /// <para><b>线程</b>：全部成员限主线程调用。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>实现方的两条宽松契约</b>（门面对它们做了豁免，第三方实现必须照做，否则会打乱调用方的
    /// 空引用安全感）：</para>
    /// <para>① <see cref="Stop"/> 与 <see cref="IsPlaying"/> 是<b>探测型</b>成员——句柄的生命周期可以长于
    /// 管理器，实现必须让「过期句柄 / 未知句柄」安全返回 <c>false</c> 而不是抛异常。</para>
    /// <para>② 加载失败<b>不抛</b>：返回 <c>default(AudioHandle)</c> 并自行记日志（与 <c>XAsset</c> 的
    /// 「失败返回 default、不抛」同一契约）。参数非法（location 为 null/空白）才抛
    /// <see cref="ArgumentException"/>。</para>
    /// </remarks>
    public interface IAudioManager : IDisposable
    {
        #region 播放

        /// <summary>
        /// 播放一个音频资源（fire-and-forget），立即返回句柄。
        /// <para>资源经 <c>location</c> 异步加载；本方法在<b>预占播放源时</b>就返回句柄，
        /// <b>加载失败时该句柄会失效</b>（用 <see cref="IsPlaying"/> 查询）。需要「拿到句柄时必定已加载成功」
        /// 请用 <see cref="PlayAsync"/>。</para>
        /// <para>方法内部已做异常收口（取消静默、其余记 <c>[Audio]</c> 错误日志），调用方无需（也不应）
        /// 再包 <c>.Forget()</c>。一次性音效用它最顺手。</para>
        /// </summary>
        /// <param name="location">资源地址（中间件实现可解释为事件名）。为 null/空白时抛 <see cref="ArgumentException"/>。</param>
        /// <param name="options">播放参数；省略时等价于 <see cref="AudioPlayOptions.Default"/>。</param>
        /// <returns>播放句柄；播放源耗尽或加载失败时为 <c>default(AudioHandle)</c>。</returns>
        AudioHandle Play(string location, AudioPlayOptions options = default);

        /// <summary>
        /// 播放一个音频资源并等待加载完成。
        /// <para>句柄在<b>加载成功之后</b>才返回，故调用方拿到的句柄必定可用；加载失败返回
        /// <c>default(AudioHandle)</c>（不抛）。</para>
        /// </summary>
        /// <param name="location">资源地址（中间件实现可解释为事件名）。为 null/空白时抛 <see cref="ArgumentException"/>。</param>
        /// <param name="options">播放参数；省略时等价于 <see cref="AudioPlayOptions.Default"/>。</param>
        /// <param name="cancellationToken">取消令牌。取消时抛 <see cref="OperationCanceledException"/>，
        /// 且不泄漏播放源与已加载的资源句柄。</param>
        /// <returns>播放句柄；播放源耗尽或加载失败时为 <c>default(AudioHandle)</c>。</returns>
        UniTask<AudioHandle> PlayAsync(string location, AudioPlayOptions options = default,
            CancellationToken cancellationToken = default);

        #endregion

        #region 控制

        /// <summary>
        /// 停止一次播放并释放它占用的播放源与资源。
        /// <para><b>探测型成员</b>：对 <c>default(AudioHandle)</c>、过期句柄或未知句柄安全返回
        /// <c>false</c>，不抛。重复调用幂等。</para>
        /// </summary>
        /// <param name="handle">播放句柄。</param>
        /// <returns>真的停止了这次播放时返回 <c>true</c>。</returns>
        bool Stop(AudioHandle handle);

        /// <summary>
        /// 停止全部播放，或只停止某个通道下的播放。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c> 表示不限通道（全部停止）。</param>
        /// <returns>实际停止的播放数量。</returns>
        int StopAll(string channel = null);

        /// <summary>
        /// 暂停所有播放。
        /// <para>暂停不释放播放源，也不停止音频资源的保活；<see cref="Resume"/> 可原样恢复。
        /// 暂停中的播放<b>不会</b>被自动回收。</para>
        /// <para>本框架不碰 <c>AudioListener.pause</c>——那是项目的全局开关；项目用它暂停时，框架的播放会
        /// 被连带暂停（这是期望行为）。</para>
        /// </summary>
        void Pause();

        /// <summary>恢复被 <see cref="Pause"/> 暂停的播放。</summary>
        void Resume();

        /// <summary>当前是否处于暂停状态。</summary>
        bool IsPaused { get; }

        #endregion

        #region 音量

        /// <summary>
        /// 主音量，线性值，钳到 <c>[0, 1]</c>。
        /// <para>变更会立即作用到<b>正在播放</b>的声音上（不是逐帧重算，而是改一次扫一遍活跃播放源）。</para>
        /// </summary>
        float MasterVolume { get; set; }

        /// <summary>主静音。为 <c>true</c> 时实际音量按 <c>0</c> 处理，但不停止播放。</summary>
        bool MasterMuted { get; set; }

        /// <summary>
        /// 设置通道音量（线性，钳到 <c>[0, 1]</c>）。通道不存在时惰性建状态。
        /// <para>变更会立即作用到该通道下正在播放的声音。</para>
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        /// <param name="volume">音量，钳到 <c>[0, 1]</c>。</param>
        /// <exception cref="ArgumentException"><paramref name="channel"/> 是保留名 <c>"master"</c> 时抛出
        /// ——总音量请用 <see cref="MasterVolume"/>。</exception>
        void SetChannelVolume(string channel, float volume);

        /// <summary>
        /// 读通道音量。<b>查询型成员</b>：不会创建通道状态；未声明的通道返回默认值 <c>1</c>。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        float GetChannelVolume(string channel);

        /// <summary>
        /// 设置通道静音。通道不存在时惰性建状态。变更会立即作用到该通道下正在播放的声音。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        /// <param name="muted">是否静音。</param>
        /// <exception cref="ArgumentException"><paramref name="channel"/> 是保留名 <c>"master"</c> 时抛出。</exception>
        void SetChannelMuted(string channel, bool muted);

        /// <summary>
        /// 读通道静音状态。<b>查询型成员</b>：不会创建通道状态；未声明的通道返回 <c>false</c>。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        bool IsChannelMuted(string channel);

        /// <summary>
        /// 声明一个通道的初始配置（音量与静音），在初始化阶段调用最合适。
        /// <para><b>不声明也能用</b>：未声明的通道首次被写入时按默认值（音量 <c>1</c>、不静音）惰性建状态。</para>
        /// <para>配置在调用时读取一次并快照，之后修改 <see cref="AudioChannelConfig"/> 实例不再生效。
        /// 对已存在的通道重复声明会覆盖它当前的音量与静音。</para>
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        /// <param name="config">初始配置，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException"><paramref name="channel"/> 是保留名 <c>"master"</c> 时抛出。</exception>
        void RegisterChannel(string channel, AudioChannelConfig config);

        #endregion

        #region 查询

        /// <summary>
        /// 查询某次播放当前是否在播。
        /// <para><b>探测型成员</b>：对 <c>default(AudioHandle)</c>、过期句柄或未知句柄返回 <c>false</c>，不抛。
        /// 加载中的播放不算「在播」。</para>
        /// </summary>
        /// <param name="handle">播放句柄。</param>
        bool IsPlaying(AudioHandle handle);

        /// <summary>当前活跃的播放数量（含加载中与已暂停的）。</summary>
        int ActiveVoiceCount { get; }

        /// <summary>某个通道下当前活跃的播放数量。未声明的通道返回 <c>0</c>。</summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        int GetActiveVoiceCount(string channel);

        #endregion
    }
}
