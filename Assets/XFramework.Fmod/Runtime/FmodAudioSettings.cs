using System;
using UnityEngine;
using XFramework.XAudio;

namespace XFramework.XAudio.Fmod
{
    /// <summary>
    /// FMOD 扩展的配置资产。在 <c>Resources</c> 中放一份即可启用自动安装（Phase 2 生效）。
    /// <para><b>这是配置数据，不是控制 API</b>：第三方控制音效只经 <see cref="AudioManager"/>；
    /// 本类型只提供 Inspector 编辑入口与 internal 快照属性。</para>
    /// <para><b>Phase 1</b>：真实集成程序集尚未实现，本资产当前不产生运行时效果。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "XFramework/FMOD Audio Settings", fileName = "FmodAudioSettings")]
    public sealed class FmodAudioSettings : ScriptableObject
    {
        #region Private Fields

        [Header("启用")]
        [SerializeField]
        [Tooltip("关闭后自动安装直接跳过，AudioManager 保持项目自己的默认行为。")]
        private bool _enabled = true;

        [Header("事件")]
        [SerializeField]
        [Tooltip("传给 provider 的事件路径前缀；默认 event:/。留空表示不补前缀。")]
        private string _eventPathPrefix = "event:/";

        [Header("Bank")]
        [SerializeField]
        [Tooltip("启动时加载的 bank 名/路径；Phase 2 生效。")]
        private string[] _banks = Array.Empty<string>();

        [SerializeField]
        [Tooltip("加载 bank 时是否同时加载采样数据；语义由 FMOD 定义。")]
        private bool _loadBankSamples;

        [SerializeField]
        [Tooltip("Dispose 时是否卸载本 provider 自己新加载的 bank；项目预加载的 bank 永不卸载。")]
        private bool _ownsBanks;

        [Header("核心 Audio 选项")]
        [SerializeField]
        [Tooltip("AudioManager 的同时播放上限；钳到至少 1。")]
        private int _maxVoices = 32;

        [SerializeField]
        [Tooltip("初始主音量；钳到 [0,1]。")]
        private float _masterVolume = 1f;

        [SerializeField]
        [Tooltip("初始主静音。")]
        private bool _masterMuted;

        #endregion

        #region Internal Properties

        /// <summary>是否启用自动安装。</summary>
        internal bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>事件路径前缀；空字符串表示不补前缀，<c>null</c> 归一为 <c>"event:/"</c>。</summary>
        internal string EventPathPrefix
        {
            get => _eventPathPrefix ?? "event:/";
            set => _eventPathPrefix = value;
        }

        /// <summary>启动时加载的 bank；<c>null</c> 归一为空数组。</summary>
        internal string[] Banks
        {
            get => _banks ?? Array.Empty<string>();
            set => _banks = value;
        }

        /// <summary>加载 bank 时是否同时加载采样数据。</summary>
        internal bool LoadBankSamples
        {
            get => _loadBankSamples;
            set => _loadBankSamples = value;
        }

        /// <summary>Dispose 时是否卸载本 provider 自己新加载的 bank。</summary>
        internal bool OwnsBanks
        {
            get => _ownsBanks;
            set => _ownsBanks = value;
        }

        /// <summary>同时播放上限；钳到至少 1。</summary>
        internal int MaxVoices
        {
            get => _maxVoices < 1 ? 1 : _maxVoices;
            set => _maxVoices = value;
        }

        /// <summary>初始主音量；钳到 <c>[0,1]</c>。</summary>
        internal float MasterVolume
        {
            get => Mathf.Clamp01(_masterVolume);
            set => _masterVolume = value;
        }

        /// <summary>初始主静音。</summary>
        internal bool MasterMuted
        {
            get => _masterMuted;
            set => _masterMuted = value;
        }

        #endregion

        #region Internal

        /// <summary>构造 provider 需要的不可变快照。</summary>
        internal FmodProviderOptions CreateProviderOptions()
        {
            return new FmodProviderOptions(EventPathPrefix, MaxVoices);
        }

        /// <summary>构造核心 Audio 初始化选项。</summary>
        internal AudioInitOptions CreateAudioInitOptions()
        {
            return new AudioInitOptions
            {
                MaxVoices = MaxVoices,
                MasterVolume = MasterVolume,
                MasterMuted = MasterMuted,
            };
        }

        #endregion
    }
}
