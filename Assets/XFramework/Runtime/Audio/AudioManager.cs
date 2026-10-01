using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XAudio
{
    /// <summary>
    /// 音频管理器外观。提供静态方法直接访问播放、通道音量与查询能力。
    /// <para>内部持有 <see cref="IAudioManager"/> 实例（默认 <see cref="AudioManagerImpl"/>），所有调用委托到该实例。</para>
    /// <para>使用前需调用 <see cref="Initialize"/> 或 <see cref="SetInstance"/>。</para>
    /// <para><b>资源一律以 location 字符串标识</b>，经 <c>XAsset</c> 异步加载——本模块不提供直接传
    /// <c>AudioClip</c> 的重载。这带来两点：界面上的公开面<b>不含任何 Unity 内置音频类型</b>，第三方可以把
    /// 整套底层换掉（见 <see cref="IAudioManager"/>）；以及每次播放的资源生命周期只有一套语义，
    /// 调用方不会因为「自己释放了资源」而让声音静默中断。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 1. 初始化（零配置即可用）
    /// AudioManager.Initialize();
    ///
    /// // 2. 一次性音效：fire-and-forget，异常已由框架收口
    /// AudioManager.Play("audio/sfx_hit", new AudioPlayOptions(AudioChannels.Se, 0.8f));
    ///
    /// // 3. 背景音乐：拿到句柄以便停止
    /// var bgm = await AudioManager.PlayAsync("audio/bgm_main",
    ///     new AudioPlayOptions(AudioChannels.Bgm, loop: true));
    /// bgm.Stop();
    ///
    /// // 4. 音量
    /// AudioManager.MasterVolume = 0.7f;
    /// AudioManager.SetChannelVolume(AudioChannels.Se, 0.5f);
    /// </code>
    /// </example>
    public static class AudioManager
    {
        #region Static — Global Singleton

        private static IAudioManager _instance;
        private static bool _instanceInitialized;

        /// <summary>
        /// 音频管理器是否已初始化。
        /// </summary>
        public static bool IsInitialized => _instanceInitialized && _instance != null;

        /// <summary>
        /// 初始化音频管理器，创建默认实现（<see cref="AudioManagerImpl"/>）。
        /// <para>零配置即可用：不传 <paramref name="options"/> 时同时播放上限为 32、主音量为满、播放源惰性创建，
        /// 从不播放的项目不会产生任何 GameObject。</para>
        /// <para>重复调用会被忽略并记一条 <c>[Audio]</c> 告警。</para>
        /// </summary>
        /// <param name="options">初始化选项；为 <c>null</c> 时使用全部默认值。</param>
        public static void Initialize(AudioInitOptions options = null)
        {
            if (_instanceInitialized)
            {
                LogManager.Warning(LogCategories.Audio, "Initialize was called more than once. Ignoring duplicate.");
                return;
            }

            _instance = new AudioManagerImpl(options);
            _instanceInitialized = true;
        }

        /// <summary>
        /// 注入自定义 <see cref="IAudioManager"/> 实现（用于测试、依赖注入，或用 Wwise / FMOD 等音频中间件
        /// 整体替换底层播放引擎）。
        /// <para><b>本方法不会 Dispose 先前持有的实例</b>（与 <c>LocalizationManager.SetInstance</c> 同一取舍）：
        /// 若之前已 <see cref="Initialize"/>，请先 <see cref="Destroy"/> 再注入，否则旧实现自持的资源不会释放。</para>
        /// <para>框架以强引用持有注入的实例，其生命周期由调用方管理；<see cref="Destroy"/> 会调用它的
        /// <see cref="IDisposable.Dispose"/>。</para>
        /// </summary>
        /// <param name="instance">自定义实现实例，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="instance"/> 为 null 时抛出。</exception>
        public static void SetInstance(IAudioManager instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            _instance = instance;
            _instanceInitialized = true;
        }

        /// <summary>
        /// 丢弃门面持有的 <see cref="IAudioManager"/> 实例，并调用它的 <see cref="IDisposable.Dispose"/>。
        /// <para>销毁会停止全部播放并释放自持的宿主与播放源池；此后需重新 <see cref="Initialize"/> 或
        /// <see cref="SetInstance"/> 才能使用。</para>
        /// <para><b>销毁后旧句柄仍然安全</b>：<see cref="Stop"/> 与 <see cref="IsPlaying"/> 会返回 <c>false</c>
        /// 而不是抛异常——它们的生命周期可以长于管理器。</para>
        /// <para>本方法也会被 <c>Application.quitting</c> 调用（见 <see cref="AutoInit"/>），且在未初始化时
        /// 调用是安全的。</para>
        /// </summary>
        public static void Destroy()
        {
            var instance = _instance;
            _instance = null;
            _instanceInitialized = false;
            instance?.Dispose();
        }

        #endregion

        #region Public API — Playback

        /// <summary>
        /// 播放一个音频资源（fire-and-forget），立即返回句柄。
        /// <para>资源经 <paramref name="location"/> 异步加载；本方法在<b>预占播放源时</b>就返回句柄，
        /// <b>加载失败时该句柄会失效</b>（用 <see cref="IsPlaying"/> 查询）。需要「拿到句柄时必定已加载成功」
        /// 请用 <see cref="PlayAsync"/>。</para>
        /// <para>方法内部已做异常收口（取消静默、其余记 <c>[Audio]</c> 错误日志），调用方无需也不应再包
        /// <c>.Forget()</c>。一次性音效用它最顺手。</para>
        /// </summary>
        /// <param name="location">资源地址。为 null/空白时抛 <see cref="ArgumentException"/>。</param>
        /// <param name="options">播放参数；省略时等价于 <see cref="AudioPlayOptions.Default"/>。</param>
        /// <returns>播放句柄；播放源耗尽或加载失败时为 <c>default(AudioHandle)</c>。</returns>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        /// <exception cref="ArgumentException"><paramref name="location"/> 为 null/空白时抛出。</exception>
        public static AudioHandle Play(string location, AudioPlayOptions options = default)
        {
            EnsureInitialized();
            return _instance.Play(location, options);
        }

        /// <summary>
        /// 播放一个音频资源并等待加载完成。
        /// <para>句柄在加载成功之后才返回，故调用方拿到的句柄必定可用；加载失败返回
        /// <c>default(AudioHandle)</c>（不抛）。</para>
        /// </summary>
        /// <param name="location">资源地址。为 null/空白时抛 <see cref="ArgumentException"/>。</param>
        /// <param name="options">播放参数；省略时等价于 <see cref="AudioPlayOptions.Default"/>。</param>
        /// <param name="cancellationToken">取消令牌。取消时抛 <see cref="OperationCanceledException"/>，
        /// 且不泄漏播放源与已加载的资源句柄。</param>
        /// <returns>播放句柄；播放源耗尽或加载失败时为 <c>default(AudioHandle)</c>。</returns>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        /// <exception cref="ArgumentException"><paramref name="location"/> 为 null/空白时抛出。</exception>
        /// <exception cref="OperationCanceledException">令牌被取消时抛出。</exception>
        public static UniTask<AudioHandle> PlayAsync(string location, AudioPlayOptions options = default,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _instance.PlayAsync(location, options, cancellationToken);
        }

        #endregion

        #region Public API — Control

        /// <summary>
        /// 停止一次播放并释放它占用的播放源与资源。
        /// <para><b>探测型成员</b>：对 <c>default(AudioHandle)</c>、过期句柄或未知句柄安全返回 <c>false</c>，
        /// 未初始化或已销毁时同样返回 <c>false</c> 而不抛——句柄的生命周期可以长于管理器。</para>
        /// </summary>
        /// <param name="handle">播放句柄。</param>
        /// <returns>真的停止了这次播放时返回 <c>true</c>。</returns>
        public static bool Stop(AudioHandle handle)
        {
            var instance = _instance;
            return instance != null && instance.Stop(handle);
        }

        /// <summary>
        /// 停止全部播放，或只停止某个通道下的播放。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c> 表示不限通道（全部停止）。</param>
        /// <returns>实际停止的播放数量。</returns>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        public static int StopAll(string channel = null)
        {
            EnsureInitialized();
            return _instance.StopAll(channel);
        }

        /// <summary>
        /// 暂停所有播放。暂停不释放播放源，也不停止资源保活；<see cref="Resume"/> 可原样恢复。
        /// <para>暂停中的播放不会被自动回收。</para>
        /// <para><b>切后台不会自动暂停</b>：本模块不接 <c>OnApplicationPause</c>，需要的话请由项目在自己的
        /// 生命周期回调里调用本方法——「是谁暂停的、谁来恢复」必须由项目掌握。</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        public static void Pause()
        {
            EnsureInitialized();
            _instance.Pause();
        }

        /// <summary>
        /// 恢复被 <see cref="Pause"/> 暂停的播放。
        /// </summary>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        public static void Resume()
        {
            EnsureInitialized();
            _instance.Resume();
        }

        /// <summary>当前是否处于暂停状态。</summary>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        public static bool IsPaused
        {
            get
            {
                EnsureInitialized();
                return _instance.IsPaused;
            }
        }

        #endregion

        #region Public API — Volume

        /// <summary>
        /// 主音量，线性值，钳到 <c>[0, 1]</c>。变更立即作用到正在播放的声音上。
        /// <para>主音量与通道音量、单次播放的音量缩放三者<b>相乘</b>得到实际音量。</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        public static float MasterVolume
        {
            get
            {
                EnsureInitialized();
                return _instance.MasterVolume;
            }
            set
            {
                EnsureInitialized();
                _instance.MasterVolume = value;
            }
        }

        /// <summary>主静音。为 <c>true</c> 时实际音量按 <c>0</c> 处理，但不停止播放。</summary>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        public static bool MasterMuted
        {
            get
            {
                EnsureInitialized();
                return _instance.MasterMuted;
            }
            set
            {
                EnsureInitialized();
                _instance.MasterMuted = value;
            }
        }

        /// <summary>
        /// 设置通道音量（线性，钳到 <c>[0, 1]</c>）。通道不存在时惰性建状态。变更立即作用到该通道下正在播放的声音。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        /// <param name="volume">音量。</param>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        /// <exception cref="ArgumentException"><paramref name="channel"/> 是保留名 <c>"master"</c> 时抛出。</exception>
        public static void SetChannelVolume(string channel, float volume)
        {
            EnsureInitialized();
            _instance.SetChannelVolume(channel, volume);
        }

        /// <summary>
        /// 读通道音量。查询型成员：不会创建通道状态，未声明的通道返回默认值 <c>1</c>。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        public static float GetChannelVolume(string channel)
        {
            EnsureInitialized();
            return _instance.GetChannelVolume(channel);
        }

        /// <summary>
        /// 设置通道静音。通道不存在时惰性建状态。变更立即作用到该通道下正在播放的声音。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        /// <param name="muted">是否静音。</param>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        /// <exception cref="ArgumentException"><paramref name="channel"/> 是保留名 <c>"master"</c> 时抛出。</exception>
        public static void SetChannelMuted(string channel, bool muted)
        {
            EnsureInitialized();
            _instance.SetChannelMuted(channel, muted);
        }

        /// <summary>
        /// 读通道静音状态。查询型成员：不会创建通道状态，未声明的通道返回 <c>false</c>。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        public static bool IsChannelMuted(string channel)
        {
            EnsureInitialized();
            return _instance.IsChannelMuted(channel);
        }

        /// <summary>
        /// 声明一个通道的初始配置（音量与静音）。不声明也能用——未声明的通道首次被写入时按默认值惰性建状态。
        /// </summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        /// <param name="config">初始配置，不可为 null。</param>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException"><paramref name="channel"/> 是保留名 <c>"master"</c> 时抛出。</exception>
        public static void RegisterChannel(string channel, AudioChannelConfig config)
        {
            EnsureInitialized();
            _instance.RegisterChannel(channel, config);
        }

        #endregion

        #region Public API — Query

        /// <summary>
        /// 查询某次播放当前是否在播。
        /// <para><b>探测型成员</b>：对 <c>default(AudioHandle)</c>、过期句柄或未知句柄返回 <c>false</c>，
        /// 未初始化或已销毁时同样返回 <c>false</c> 而不抛。加载中的播放不算「在播」。</para>
        /// </summary>
        /// <param name="handle">播放句柄。</param>
        public static bool IsPlaying(AudioHandle handle)
        {
            var instance = _instance;
            return instance != null && instance.IsPlaying(handle);
        }

        /// <summary>当前活跃的播放数量（含加载中与已暂停的）。</summary>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        public static int ActiveVoiceCount
        {
            get
            {
                EnsureInitialized();
                return _instance.ActiveVoiceCount;
            }
        }

        /// <summary>某个通道下当前活跃的播放数量。未声明的通道返回 <c>0</c>。</summary>
        /// <param name="channel">通道名；<c>null</c>/空归一化为 <see cref="AudioChannels.Default"/>。</param>
        /// <exception cref="InvalidOperationException">未初始化时抛出。</exception>
        public static int GetActiveVoiceCount(string channel)
        {
            EnsureInitialized();
            return _instance.GetActiveVoiceCount(channel);
        }

        #endregion

        #region Internal

        /// <summary>
        /// 未初始化时抛 <see cref="InvalidOperationException"/>，消息带 <c>[Audio]</c> 前缀与修复提示。
        /// <para><b>探测型成员不走这里</b>（<see cref="Stop"/>、<see cref="IsPlaying"/>）——句柄的生命周期
        /// 可以长于管理器，它们必须安全返回 <c>false</c>。</para>
        /// </summary>
        private static void EnsureInitialized()
        {
            if (!_instanceInitialized || _instance == null)
            {
                throw new InvalidOperationException(
                    "[Audio] 音频管理器尚未初始化。请先调用 AudioManager.Initialize() 或 " +
                    "AudioManager.SetInstance(...) 完成初始化。");
            }
        }

        /// <summary>
        /// 自动初始化：补挂「退出时清理」的订阅，无需外部调用（幂等）。
        /// <para><b>两个特性都要挂：</b>编辑器里 <see cref="UnityEditor.InitializeOnLoadMethodAttribute"/>
        /// 只在程序集加载（含重编译引发的域重载）时执行；而在 Project Settings → Editor →
        /// Enter Play Mode Options 里关闭 Reload Domain 后，进入播放<b>不会</b>重新加载程序集，该回调
        /// 不再执行。<see cref="RuntimeInitializeOnLoadMethodAttribute"/> 在进入播放时同样会执行，
        /// 是关闭域重载时唯一能补挂它的时机。</para>
        /// <para><b>订阅必须幂等：</b>开启域重载时进入播放会先后触发两者；关闭域重载时
        /// <c>Application.quitting</c> 的订阅表还会跨播放会话存活，重复 <c>+=</c> 会逐次累积。
        /// 下面用「先 <c>-=</c> 再 <c>+=</c>」保证恰好一条——**因此 <see cref="Destroy"/> 不自退订**：
        /// 那行的正确性完全依赖这里的 <c>-=</c> 先执行，删掉它这条保证就没了。</para>
        /// <para><b>档位用默认的 <see cref="RuntimeInitializeLoadType.AfterSceneLoad"/></b>：本方法只做订阅，
        /// 不注入 PlayerLoop、也不需要在首个场景 Awake 之前就绪。想改档位请同步更新
        /// <c>Tests/Runtime/Architecture/AutoInitTests</c> 的族清单，否则守卫会红。</para>
        /// </summary>
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod]
        internal static void AutoInit()
        {
            Application.quitting -= Destroy;
            Application.quitting += Destroy;
        }

        #endregion
    }
}
