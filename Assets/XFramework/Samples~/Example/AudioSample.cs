using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XAudio;
using XFramework.XLog;

namespace XFramework.Example
{
    /// <summary>
    /// 展示 XFramework Audio 模块的用法：通道、三档音量、播放句柄、暂停。
    /// <para>把它挂到场景里的任意 GameObject 上即可运行。BGM 与音效都是 <b>location 字符串</b>——
    /// 音频模块只走 <c>XAsset</c> 加载，不提供直接传 <c>AudioClip</c> 的重载。用示例值时请先把这几个
    /// 地址注册进 YooAsset（或换成你自己项目里已有的地址）。</para>
    /// </summary>
    public class AudioSample : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private string _bgmLocation = "audio/bgm_main";
        [SerializeField] private string _hitLocation = "audio/sfx_hit";
        [SerializeField] private string _uiClickLocation = "audio/sfx_ui_click";

        #endregion

        #region Private Fields

        private AudioHandle _bgm;

        #endregion

        #region Unity Lifecycle

        private async void Start()
        {
            // 音频资源只走 XAsset（YooAsset）地址加载。没有资源管线时先退出，
            // 否则加载失败会被 Audio 模块收口成一条红色 Error，示例的场景就「不干净」了。
            if (!AssetManager.IsInitialized ||
                !AssetManager.CheckLocationValid(_bgmLocation) ||
                !AssetManager.CheckLocationValid(_hitLocation) ||
                !AssetManager.CheckLocationValid(_uiClickLocation))
            {
                LogManager.Warning(SampleLogCategories.Example,
                    "AudioSample 跳过播放：需要 YooAsset 资源地址 '{0}'（先完成采集配置）。", _bgmLocation);
                return;
            }

            // 1. 初始化。零配置可用：同时播放上限 32、主音量满、播放源惰性创建
            //    （从不播放的项目一个 GameObject 都不会建）
            AudioManager.Initialize();
            IsReady = true;

            // 2. 一次性音效：fire-and-forget。异常已由框架收口，调用方不必（也不应）再包 .Forget()
            AudioManager.Play(_hitLocation, new AudioPlayOptions(AudioChannels.SE, volumeScale: 0.8f));

            // 3. 3D 音效：只有在 spatialBlend > 0 时位置才会被写进播放源
            AudioManager.Play(_hitLocation, new AudioPlayOptions(
                AudioChannels.SE, loop: false, spatialBlend: 1f, position: transform.position));

            // 4. BGM：需要句柄以便停止，故用 PlayAsync——它的句柄返回时必定已加载成功
            try
            {
                _bgm = await AudioManager.PlayAsync(_bgmLocation,
                    new AudioPlayOptions(AudioChannels.Bgm, volumeScale: 0.6f, loop: true),
                    destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 加载途中对象被销毁：句柄没拿到，也就没有需要停的东西
                return;
            }

            if (_bgm.IsDefault)
                LogManager.Warning(SampleLogCategories.Example,
                    "BGM 未能起播：'{0}'——确认该地址已注册进 YooAsset。", _bgmLocation);
        }

        private void OnDestroy()
        {
            // 5. 循环播放必须显式停止——它永远不会「播完」，也就不会被自动回收
            _bgm.Stop();
        }

        #endregion

        #region 演示：接到 UI 上的几个入口

        /// <summary>
        /// 是否已检测到音频地址并完成 AudioManager 初始化。
        /// <para>未就绪时下面的入口都是安全空操作——场景里挂了本组件但没有资源管线也不会报错。</para>
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>设置面板：音效通道音量。</summary>
        public void SetSEVolume(float volume)
        {
            if (AudioManager.IsInitialized)
                AudioManager.SetChannelVolume(AudioChannels.SE, volume);
        }

        /// <summary>设置面板：一键静音人声。静音只是听不见，播放不会中断。</summary>
        public void SetVoiceMuted(bool muted)
        {
            if (AudioManager.IsInitialized)
                AudioManager.SetChannelMuted(AudioChannels.Voice, muted);
        }

        /// <summary>设置面板：总音量。</summary>
        public void SetMasterVolume(float volume)
        {
            if (AudioManager.IsInitialized)
                AudioManager.MasterVolume = volume;
        }

        /// <summary>暂停菜单：暂停/恢复全部音频（含暂停期间新起的声音）。</summary>
        public void SetPaused(bool paused)
        {
            if (!AudioManager.IsInitialized)
                return;

            if (paused)
                AudioManager.Pause();
            else
                AudioManager.Resume();
        }

        /// <summary>按钮：播一个界面音。</summary>
        public void PlayUIClick()
        {
            if (AudioManager.IsInitialized)
                AudioManager.Play(_uiClickLocation, AudioPlayOptions.Default);
        }

        /// <summary>调试用：当前有几路声音在播（含加载中与已暂停的）。</summary>
        public int ActiveVoices => AudioManager.IsInitialized ? AudioManager.ActiveVoiceCount : 0;

        #endregion

        #region 与 Settings 模块接线（可选，故只留成注释）

        // 音频模块**不依赖** Settings——那是个可选模块，音频的初始化不该被它的初始化顺序绑住。
        // 要把音量做进设置里，项目侧接三行即可：
        //
        //   private static readonly SettingRef<GameSettings, float> BgmVolume =
        //       SettingsManager.Ref<GameSettings, float>(s => s.Audio.BgmVolume);   // 调用一次并缓存
        //
        //   BgmVolume.Subscribe(v => AudioManager.SetChannelVolume(AudioChannels.Bgm, v));
        //
        // SettingRef.Subscribe 会**立即同步回调一次当前值**，正好补上「启动时把已存档音量推给音频」
        // 这一步，不必额外读一次。
        //
        // 反之，若项目要用自己的 AudioMixer 总线控音量：把通道音量留在 1.0，让 Mixer 独占控制即可
        // （框架只写自己池里播放源的 volume，不碰 Mixer）。

        #endregion
    }
}
