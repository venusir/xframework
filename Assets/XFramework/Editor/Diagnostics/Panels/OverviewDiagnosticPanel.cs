using XFramework.XAsset;
using XFramework.XAudio;
using XFramework.XBootstrap;
using XFramework.XConfig;
using XFramework.XData;
using XFramework.XDiagnostics;
using XFramework.XFileManager;
using XFramework.XInput;
using XFramework.XLocalization;
using XFramework.XLock;
using XFramework.XLog;
using XFramework.XMessage;
using XFramework.XPool;
using XFramework.XSave;
using XFramework.XSerialize;
using XFramework.XSettings;
using XFramework.XTimer;
using XFramework.XUI;
using XFramework.XUpdate;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// 概览页签：一张模块状态矩阵——哪些模块已初始化、各自的规模一句话。
    /// <para>它是诊断窗口的落地点：先在这里看「谁没起来」，再进对应页签看细节。</para>
    /// <para><b>为什么这里能安全地问未初始化的模块</b>：所有探测型读（<c>IsInitialized</c> 一族与
    /// 各模块的计数）都按契约在未初始化时返回默认值而不是抛异常；需要显式初始化的模块在各自页签里
    /// 另有「未初始化」说明。</para>
    /// </summary>
    internal sealed class OverviewDiagnosticPanel : IDiagnosticPanel
    {
        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "概览";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("模块状态");

            var table = report.BeginTable(null, "模块", "状态", "规模");

            Row(table, "UI", UIManager.IsInitialized, UIManager.IsInitialized ? $"{UIManager.OpenCount} 个面板" : "-");
            Row(table, "Asset", AssetManager.IsInitialized, "-");
            Row(table, "Config", ConfigManager.IsInitialized, "-");
            Row(table, "Data", DataManager.IsInitialized, "-");
            Row(table, "File", FileManager.IsInitialized, "-");
            Row(table, "Input", InputManager.IsInitialized, "-");
            Row(table, "Localization", LocalizationManager.IsInitialized, "-");
            Row(table, "Save", SaveManager.IsInitialized, "-");
            Row(table, "Settings", SettingsManager.IsInitialized, $"{SettingsManager.RegisteredTypeCount} 个类型");
            Row(table, "Serialize", Serializer.IsInitialized, "-");
            Row(table, "Audio", AudioManager.IsInitialized, AudioManager.IsInitialized ? $"{AudioManager.ActiveVoiceCount} 路在播" : "-");
            Row(table, "Update", UpdateManager.IsInitialized, $"{UpdateManager.TotalCount} 个节点");
            Row(table, "Log", LogManager.IsInitialized, $"{LogCategory.RegisteredCount} 个分类");

            // 纯静态服务：没有 IsInitialized（首次使用即自初始化），状态栏直接写「静态服务」
            StaticRow(table, "Message", $"{MessageManager.GetStats().ChannelCount} 个通道");
            StaticRow(table, "Lock", $"{LockManager.GetSnapshot().LockCount} 个锁");
            StaticRow(table, "Pool", $"{PoolManager.PoolCount} 个池");
            StaticRow(table, "Timer", $"{TimerManager.ActiveCount} 个在计时");
            StaticRow(table, "Bootstrap", $"{Bootstrap.Stages.Count} 个阶段");

            report.Notice(DiagnosticLevel.Info,
                "Event / Reactive / Pipeline 是实例制或按类型持有，框架没有它们的全局注册表，因此不在这张表里。");
        }

        #endregion

        #region Private

        private static void Row(DiagnosticTable table, string module, bool initialized, string scale)
        {
            table.AddRow(module, initialized ? "已初始化" : "未初始化", initialized ? scale : "-");
        }

        private static void StaticRow(DiagnosticTable table, string module, string scale)
        {
            table.AddRow(module, "静态服务", scale);
        }

        #endregion
    }
}
