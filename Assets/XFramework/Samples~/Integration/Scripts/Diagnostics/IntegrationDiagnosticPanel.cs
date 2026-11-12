using System.Collections.Generic;
using XFramework.XAsset;
using XFramework.XAudio;
using XFramework.XCamera;
using XFramework.XConfig;
using XFramework.XData;
using XFramework.XDiagnostics;
using XFramework.XInput;
using XFramework.XLocalization;
using XFramework.XMessage;
using XFramework.XPool;
using XFramework.XSave;
using XFramework.XSettings;
using XFramework.XTimer;
using XFramework.XUI;
using XFramework.XUpdate;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 示例诊断页签：把各模块的回读面集中到一页，方便在「工具 → XFramework → Diagnostics」里
    /// 看到示例运行时的真实状态。
    /// <para>写侧契约：主线程、可重复调用、无副作用、不得抛——所有读取都用探测型 API。</para>
    /// </summary>
    public sealed class IntegrationDiagnosticPanel : IDiagnosticPanel
    {
        private readonly List<ConfigLoadedInfo> _configs = new List<ConfigLoadedInfo>(8);
        private readonly List<LocalizationLanguageInfo> _languages = new List<LocalizationLanguageInfo>(4);
        private readonly List<PoolStats> _pools = new List<PoolStats>(8);

        /// <inheritdoc/>
        public string Title => "Integration";

        /// <inheritdoc/>
        public int Order => DiagnosticOrders.User;

        /// <inheritdoc/>
        public void Collect(IDiagnosticReport report)
        {
            report.Section("Asset");
            report.KeyValue("已初始化", AssetManager.IsInitialized.ToString());
            report.KeyValue("MainMenu 地址有效", AssetManager.CheckLocationValid(IntegrationAddresses.MainMenu).ToString());
            var pool = AssetManager.GetPoolStatus(IntegrationAddresses.MainMenu);
            report.KeyValue("MainMenu 实例池", $"pooled={pool.pooledCount} active={pool.activeCount} max={pool.maxPoolSize}");

            report.Section("数据 / 存档 / 设置");
            report.KeyValue("脏数据块", DataManager.HasDirtyBlocks.ToString());
            report.KeyValue("存档忙", SaveManager.IsBusy.ToString());
            report.KeyValue("设置脏", SettingsManager.IsDirty<IntegrationSettings>().ToString());

            report.Section("消息 / 定时器 / 更新 / 池");
            var stats = MessageManager.GetStats();
            report.KeyValue("消息发布数", stats.PublishCount.ToString());
            report.KeyValue("同步订阅", stats.SyncSubscriptionCount.ToString());
            report.KeyValue("缓冲频道", stats.BufferedChannelCount.ToString());
            report.KeyValue("活动定时器", TimerManager.ActiveCount.ToString());
            report.KeyValue("更新节点", UpdateManager.TotalCount.ToString());
            int poolCount = PoolManager.CopyPoolStats(_pools);
            report.KeyValue("对象池数", poolCount.ToString());

            report.Section("Config / Localization / Input");
            int configCount = ConfigManager.CopyLoaded(_configs);
            report.KeyValue("已加载配置", configCount.ToString());
            report.KeyValue("当前语言", LocalizationManager.CurrentLanguage ?? "(none)");
            int languageCount = LocalizationManager.CopyLoadedLanguages(_languages);
            report.KeyValue("已加载语言", languageCount.ToString());
            report.KeyValue("输入设备", InputManager.LastActiveDeviceType.ToString());
            report.KeyValue("Pause 绑定", InputManager.GetBindingDisplayString("Pause") ?? "(none)");

            report.Section("UI / Camera");
            report.KeyValue("UI 打开数", UIManager.OpenCount.ToString());
            report.KeyValue("相机数", CameraManager.CameraCount.ToString());
            report.KeyValue("活动相机", CameraManager.ActiveCameraId ?? "(none)");

            report.Section("Audio");
            report.KeyValue("活动声部", AudioManager.ActiveVoiceCount.ToString());
            report.KeyValue("主音量", AudioManager.MasterVolume.ToString("F2"));
        }
    }
}
