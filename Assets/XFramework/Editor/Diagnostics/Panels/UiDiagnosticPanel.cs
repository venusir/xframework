using System.Collections.Generic;
using XFramework.XDiagnostics;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// UI 页签：面板栈、状态快照、层级开关与完整状态转储。
    /// <para>数据全部来自 <see cref="UIManager"/> 的公开查询面（探测型读 + 活视图），
    /// 因此「窗口里看到的」与「代码里读到的」是同一份真相。</para>
    /// <para>它接替了原 <c>UI State</c> 独立窗口：后者显示的面板列表、状态快照与 <c>DumpState</c>
    /// 全文在这里一项不少，另加层级可见性/可交互回读（本轮随诊断窗口一起公开）。</para>
    /// </summary>
    internal sealed class UiDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用（CopyPanels 的零分配口径）。</summary>
        private readonly List<UIPanelBase> _panels = new List<UIPanelBase>();

        /// <summary>已知层的去重缓冲，避免每次采集重新分配。</summary>
        private readonly List<int> _layers = new List<int>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "UI";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            if (!UIManager.IsInitialized)
            {
                report.Section("UI");
                report.KeyValue("已初始化", "否");
                report.Notice(DiagnosticLevel.Info, "UIManager 尚未初始化：进入播放模式后这里才有数据。");
                return;
            }

            CollectOverview(report);
            CollectPanelList(report);
            CollectLayerList(report);
            CollectDump(report);
        }

        #endregion

        #region Sections

        private static void CollectOverview(IDiagnosticReport report)
        {
            var state = UIManager.GetState();

            report.Section("概览");
            report.KeyValue("已打开面板", state.OpenCount.ToString());
            report.KeyValue("在途打开", state.InFlightOpenCount.ToString());
            report.KeyValue("遮罩引用计数", state.MaskRefCount.ToString());
            report.KeyValue("遮罩显示中", state.IsMaskShowing ? "是" : "否");
            report.KeyValue("可返回", state.CanGoBack ? "是" : "否");
            report.KeyValue("非零档位驱动器", UIManager.TierDriverCount.ToString());
        }

        private void CollectPanelList(IDiagnosticReport report)
        {
            UIManager.CopyPanels(_panels);

            report.Section("面板（底 → 顶）");
            if (_panels.Count == 0)
            {
                report.Text("(没有已打开的面板)");
                return;
            }

            var table = report.BeginTable(null, "类型", "层", "档位", "焦点", "暂停");
            for (int i = 0; i < _panels.Count; i++)
            {
                var panel = _panels[i];
                table.AddRow(
                    panel.GetType().Name,
                    panel.Layer.ToString(),
                    panel.UpdateTier.ToString(),
                    panel.IsFocused ? "是" : "否",
                    panel.IsPaused ? "是" : "否");
            }
        }

        private void CollectLayerList(IDiagnosticReport report)
        {
            UIManager.CopyPanels(_panels);

            _layers.Clear();
            _layers.Add(UILayers.Background);
            _layers.Add(UILayers.Default);
            _layers.Add(UILayers.Popup);
            _layers.Add(UILayers.Top);
            _layers.Add(UILayers.Mask);

            // 项目可以自定义层号（UILayers 只是建议值），已打开面板所在的层一并列出
            for (int i = 0; i < _panels.Count; i++)
            {
                if (!_layers.Contains(_panels[i].Layer))
                    _layers.Add(_panels[i].Layer);
            }

            _layers.Sort();

            report.Section("层级");
            var table = report.BeginTable(null, "层", "名称", "可见", "可交互", "已打开");
            for (int i = 0; i < _layers.Count; i++)
            {
                int layer = _layers[i];
                table.AddRow(
                    layer.ToString(),
                    LayerName(layer),
                    UIManager.IsLayerVisible(layer) ? "是" : "否",
                    UIManager.IsLayerInteractive(layer) ? "是" : "否",
                    CountPanelsInLayer(layer).ToString());
            }
        }

        private static void CollectDump(IDiagnosticReport report)
        {
            report.Section("完整状态转储");
            report.Text(UIManager.DumpState());
        }

        #endregion

        #region Helpers

        private int CountPanelsInLayer(int layer)
        {
            int count = 0;
            for (int i = 0; i < _panels.Count; i++)
            {
                if (_panels[i].Layer == layer)
                    count++;
            }

            return count;
        }

        private static string LayerName(int layer)
        {
            switch (layer)
            {
                case UILayers.Background:
                    return "Background";
                case UILayers.Default:
                    return "Default";
                case UILayers.Popup:
                    return "Popup";
                case UILayers.Top:
                    return "Top";
                case UILayers.Mask:
                    return "Mask";
                default:
                    return "-";
            }
        }

        #endregion
    }
}
