using System.Collections.Generic;
using XFramework.XDiagnostics;
using XFramework.XLog;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Log 页签：全局档位、分类表（生效档位 / 是否被覆盖）、输出端列表。
    /// <para>回答「这条日志为什么没出来」——先看全局档，再看是不是被分类覆盖改了；
    /// 以及「谁在收日志」——输出端只有类型可认（<see cref="ILogSink"/> 刻意没有名字）。</para>
    /// <para>数据来自 <see cref="LogManager"/> 的回读成员；注入第三方 <see cref="ILogManager"/> 时它们
    /// 一律返回空，本页签会写一行说明（不是错误）。</para>
    /// </summary>
    internal sealed class LogDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用。</summary>
        private readonly List<LogCategoryInfo> _categories = new List<LogCategoryInfo>();

        private readonly List<ILogSink> _sinks = new List<ILogSink>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Log";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 10;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("全局最低档位", LogManager.MinimumLevel.ToString());
            report.KeyValue("分类数", LogCategory.RegisteredCount.ToString());
            report.KeyValue("输出端数", LogManager.SinkCount.ToString());
            report.KeyValue("被摘除的输出端次数", LogManager.DroppedSinkCount.ToString());

            CollectCategories(report);
            CollectSinks(report);
        }

        #endregion

        #region Sections

        private void CollectCategories(IDiagnosticReport report)
        {
            LogManager.CopyCategories(_categories);

            report.Section("分类（按名称）");
            if (_categories.Count == 0)
            {
                report.Notice(DiagnosticLevel.Info,
                    "分类表为空：当前不是内置 LogManager（第三方实现不提供回读），或还没有任何分类被使用过。");
                return;
            }

            var table = report.BeginTable(null, "分类", "生效档位", "来源");
            for (int i = 0; i < _categories.Count; i++)
            {
                var info = _categories[i];
                table.AddRow(info.Name, info.EffectiveLevel.ToString(), info.IsOverridden ? "覆盖" : "跟随全局");
            }
        }

        private void CollectSinks(IDiagnosticReport report)
        {
            LogManager.CopySinks(_sinks);

            report.Section("输出端（注册顺序）");
            if (_sinks.Count == 0)
            {
                report.Text("(没有任何输出端)");
                return;
            }

            var table = report.BeginTable(null, "类型");
            for (int i = 0; i < _sinks.Count; i++)
                table.AddRow(_sinks[i].GetType().Name);
        }

        #endregion
    }
}
