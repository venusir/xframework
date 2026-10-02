using System.Collections.Generic;
using XFramework.XDiagnostics;
using XFramework.XLocalization;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Localization 页签：当前语言、回退语言与已缓存语言表。
    /// <para>回答「切了语言没生效」「键查不到」——先看缓存里到底有什么、当前生效的是哪一条。</para>
    /// <para><b>已知限制</b>：缺失键没有计数（那要在 <c>Get</c> 热路径上加写入，本仓未做），
    /// 本页签只能给出「有哪些语言、各多少条目」。</para>
    /// </summary>
    internal sealed class LocalizationDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用。</summary>
        private readonly List<LocalizationLanguageInfo> _languages = new List<LocalizationLanguageInfo>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Localization";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 40;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("已初始化", LocalizationManager.IsInitialized ? "是" : "否");

            if (!LocalizationManager.IsInitialized)
            {
                report.Notice(DiagnosticLevel.Info, "LocalizationManager 尚未初始化：进入播放模式后这里才有数据。");
                return;
            }

            report.KeyValue("当前语言", LocalizationManager.CurrentLanguage ?? "(未设置)");
            report.KeyValue("回退语言", LocalizationManager.FallbackLanguage ?? "(未设置)");
            report.KeyValue("已缓存语言数", LocalizationManager.CachedLanguageCount.ToString());

            LocalizationManager.CopyLoadedLanguages(_languages);

            report.Section("已缓存语言（最近使用在前）");
            if (_languages.Count == 0)
            {
                report.Text("(缓存为空)");
                return;
            }

            var table = report.BeginTable(null, "语言", "条目", "角色");
            for (int i = 0; i < _languages.Count; i++)
            {
                var info = _languages[i];
                table.AddRow(
                    info.Language,
                    info.EntryCount.ToString(),
                    info.IsCurrent ? "当前" : (info.IsFallback ? "回退" : "-"));
            }
        }

        #endregion
    }
}
