using System.Collections.Generic;
using XFramework.XDiagnostics;
using XFramework.XTimer;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Timer 页签：活跃定时器明细。
    /// <para>回答「定时器为什么没触发」「哪个回调还挂着」——列出所有占用槽位的定时器，
    /// 含已 <c>Stop</c> 但未释放的那批（那是「停掉的定时器仍占着槽位」这类泄漏的唯一可见面）。</para>
    /// </summary>
    internal sealed class TimerDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用。</summary>
        private readonly List<TimerInfo> _timers = new List<TimerInfo>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Timer";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 60;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            TimerManager.CopyActiveTimers(_timers);

            report.Section("概览");
            report.KeyValue("正在计时", TimerManager.ActiveCount.ToString());
            report.KeyValue("占用槽位", _timers.Count.ToString());

            report.Section("定时器（占用槽位者）");
            if (_timers.Count == 0)
            {
                report.Text("(没有正在计时或占用槽位的定时器)");
                return;
            }

            var table = report.BeginTable(null, "轴", "槽位", "剩余", "时长", "重复", "在计时", "回调");
            for (int i = 0; i < _timers.Count; i++)
            {
                var timer = _timers[i];
                table.AddRow(
                    timer.TimeMode.ToString(),
                    timer.Slot.ToString(),
                    timer.Remaining.ToString("F2"),
                    timer.Duration.ToString("F2"),
                    timer.IsRepeating ? "是" : "-",
                    timer.IsRunning ? "是" : "停",
                    timer.CallbackName);
            }
        }

        #endregion
    }
}
