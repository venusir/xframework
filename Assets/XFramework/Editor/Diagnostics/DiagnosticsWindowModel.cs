using System;
using System.Collections.Generic;
using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics
{
    /// <summary>
    /// 诊断窗口的**可测内核**：页签表重载、选中项、节流采集、异常隔离、自绘路径选择。
    /// <para>窗口本身只剩「生命周期 + 转发本类 + GUILayout 调用」三类语句——IMGUI 调用不可测，
    /// 凡是能判断对错的东西都放在这里（时钟由调用方注入，测试因此不必等真实时间）。</para>
    /// <para>非线程安全；只在编辑器主线程使用。</para>
    /// </summary>
    internal sealed class DiagnosticsWindowModel
    {
        #region Private Fields

        private readonly List<IDiagnosticPanel> _panels = new List<IDiagnosticPanel>();
        private readonly List<string> _titles = new List<string>();
        private readonly DiagnosticReport _report = new DiagnosticReport();

        private int _selectedIndex = -1;
        private double _nextRefreshTime;
        private bool _dirty = true;

        #endregion

        #region Public Properties

        /// <summary>自动刷新开关；关闭后只有手动刷新、切换页签与页签表变化会触发采集。</summary>
        internal bool AutoRefresh { get; set; } = true;

        /// <summary>自动刷新的最小间隔（秒）。</summary>
        internal double RefreshInterval { get; set; } = 0.5;

        /// <summary>当前已登记的页签数。</summary>
        internal int PanelCount => _panels.Count;

        /// <summary>当前选中页签的下标；没有页签时为 -1。</summary>
        internal int SelectedIndex => _selectedIndex;

        /// <summary>是否至少有一个页签。</summary>
        internal bool HasPanels => _panels.Count > 0;

        /// <summary>选中页签最近一次采集的结果（由窗口直接渲染）。</summary>
        internal DiagnosticReport Report => _report;

        /// <summary>最近一次采集是否以异常告终（此时 <see cref="Report"/> 里放的是错误说明）。</summary>
        internal bool Failed { get; private set; }

        #endregion

        #region Public API

        /// <summary>
        /// 同步点：重载页签表 + 按节流判据决定是否采集。返回是否发生了采集。
        /// </summary>
        /// <param name="now">当前时刻（秒）——由调用方注入，测试用假时钟。</param>
        /// <param name="manual">true = 用户手动刷新（绕过节流与自动刷新开关）。</param>
        /// <returns>发生采集返回 true。</returns>
        internal bool Tick(double now, bool manual)
        {
            ReloadPanels();

            if (!HasPanels)
                return false;

            if (!manual && !_dirty)
            {
                if (!AutoRefresh || now < _nextRefreshTime)
                    return false;
            }

            _nextRefreshTime = now + RefreshInterval;
            _dirty = false;
            CollectSelected();
            return true;
        }

        /// <summary>选中一个页签（越界会被钳进合法区间；没有页签时忽略）。</summary>
        /// <param name="index">目标下标。</param>
        internal void Select(int index)
        {
            if (!HasPanels)
                return;

            int clamped = index < 0 ? 0 : (index >= _panels.Count ? _panels.Count - 1 : index);
            if (clamped == _selectedIndex)
                return;

            _selectedIndex = clamped;
            _dirty = true;   // 换了页签就必须重采——即便自动刷新关着
        }

        /// <summary>取某个页签的标题（重载时已缓存，标题 getter 抛异常时给占位名）。</summary>
        /// <param name="index">页签下标。</param>
        /// <returns>标题；越界返回空串。</returns>
        internal string GetTitle(int index)
        {
            return index >= 0 && index < _titles.Count ? _titles[index] : string.Empty;
        }

        /// <summary>当前选中页签是否自带渲染（<see cref="IDiagnosticPanelView"/>）。</summary>
        /// <param name="view">命中时的自绘实现。</param>
        /// <returns>命中返回 true。</returns>
        internal bool TryGetCustomView(out IDiagnosticPanelView view)
        {
            view = SelectedPanel as IDiagnosticPanelView;
            return view != null;
        }

        /// <summary>「复制」按钮的内容：与窗口显示同一份报告（异常说明也在报告里）。</summary>
        /// <returns>纯文本。</returns>
        internal string BuildCopyText()
        {
            return DiagnosticReportFormatter.ToText(_report);
        }

        #endregion

        #region Private

        private IDiagnosticPanel SelectedPanel
        {
            get { return _selectedIndex >= 0 && _selectedIndex < _panels.Count ? _panels[_selectedIndex] : null; }
        }

        private void ReloadPanels()
        {
            var previousSelected = SelectedPanel;
            int previousCount = _panels.Count;

            DiagnosticsManager.CopyPanels(_panels);

            _titles.Clear();
            for (int i = 0; i < _panels.Count; i++)
                _titles.Add(SafeTitle(_panels[i]));

            if (_panels.Count == 0)
                _selectedIndex = -1;
            else if (_selectedIndex < 0)
                _selectedIndex = 0;
            else if (_selectedIndex >= _panels.Count)
                _selectedIndex = _panels.Count - 1;

            if (previousCount != _panels.Count || !ReferenceEquals(previousSelected, SelectedPanel))
                _dirty = true;
        }

        private void CollectSelected()
        {
            var panel = SelectedPanel;
            if (panel == null)
                return;

            _report.Clear();
            Failed = false;

            try
            {
                panel.Collect(_report);
            }
            catch (Exception ex)
            {
                // 抛异常的页签：本次内容整份作废，错误说明写进报告本身——这样显示、复制、
                // 自绘三条路径看到的都是同一份东西，不必各自处理"失败了怎么办"。
                _report.Clear();
                _report.Notice(DiagnosticLevel.Error, $"页签 '{GetTitle(_selectedIndex)}' 采集时抛出异常，本次内容作废。");
                _report.Text(ex.ToString());
                Failed = true;
            }
        }

        /// <summary>
        /// 标题读取的兜底：页签在登记时 Title 是好的，但模块销毁后可能变成会抛的 getter。
        /// 页签列表一帧要读很多次，绝不能让它把整个窗口带崩。
        /// </summary>
        private static string SafeTitle(IDiagnosticPanel panel)
        {
            try
            {
                return panel.Title ?? "(无标题)";
            }
            catch (Exception)
            {
                return "(" + panel.GetType().Name + ")";
            }
        }

        #endregion
    }
}
