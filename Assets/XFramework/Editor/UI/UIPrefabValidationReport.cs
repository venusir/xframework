using System;
using System.Collections.Generic;
using System.Text;

namespace XFramework.Editor.UI
{
    /// <summary>
    /// 单个预制体的校验结果。
    /// </summary>
    internal sealed class UIPrefabValidationEntry
    {
        /// <summary>
        /// 创建一个预制体结果。
        /// </summary>
        /// <param name="assetPath">预制体资源路径。</param>
        /// <param name="assetGuid">预制体资源 GUID。</param>
        public UIPrefabValidationEntry(string assetPath, string assetGuid)
        {
            AssetPath = UIPrefabAddressEntry.NormalizePath(assetPath);
            AssetGuid = assetGuid ?? string.Empty;
        }

        /// <summary>预制体资源路径，统一使用正斜杠。</summary>
        public string AssetPath { get; }

        /// <summary>预制体资源 GUID。</summary>
        public string AssetGuid { get; }

        /// <summary>命中的 YooAsset 采集结果；未启用地址检查或未收录时为空。</summary>
        public List<UIPrefabAddressEntry> Addresses { get; } = new List<UIPrefabAddressEntry>();

        /// <summary>本预制体的全部发现。</summary>
        public List<UIPrefabIssue> Issues { get; } = new List<UIPrefabIssue>();

        /// <summary>是否存在发现。</summary>
        public bool HasIssues => Issues.Count > 0;

        /// <summary>本预制体最严重的发现等级；没有任何发现时为 <see cref="UIPrefabIssueSeverity.Info"/>。</summary>
        public UIPrefabIssueSeverity MaxSeverity
        {
            get
            {
                var max = UIPrefabIssueSeverity.Info;
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].Severity > max)
                        max = Issues[i].Severity;
                }

                return max;
            }
        }

        /// <summary>
        /// 统计指定等级的发现数量。
        /// </summary>
        /// <param name="severity">等级。</param>
        public int Count(UIPrefabIssueSeverity severity)
        {
            var count = 0;
            for (int i = 0; i < Issues.Count; i++)
            {
                if (Issues[i].Severity == severity)
                    count++;
            }

            return count;
        }
    }

    /// <summary>
    /// 全局通知：地址配置缺失、多份、采集失败、扫描取消等。
    /// </summary>
    internal sealed class UIPrefabValidationNotice
    {
        /// <summary>
        /// 创建一条全局通知。
        /// </summary>
        /// <param name="severity">等级。</param>
        /// <param name="message">说明。</param>
        public UIPrefabValidationNotice(UIPrefabIssueSeverity severity, string message)
        {
            Severity = severity;
            Message = message ?? string.Empty;
        }

        /// <summary>等级。</summary>
        public UIPrefabIssueSeverity Severity { get; }

        /// <summary>说明。</summary>
        public string Message { get; }
    }

    /// <summary>
    /// 一次扫描的完整结果。
    /// </summary>
    internal sealed class UIPrefabValidationReport
    {
        private readonly List<UIPrefabValidationEntry> _entries = new List<UIPrefabValidationEntry>();
        private readonly List<UIPrefabValidationNotice> _notices = new List<UIPrefabValidationNotice>();

        /// <summary>候选预制体结果，按严重度从高到低、路径升序排列。</summary>
        public IReadOnlyList<UIPrefabValidationEntry> Entries => _entries;

        /// <summary>全局通知。</summary>
        public IReadOnlyList<UIPrefabValidationNotice> Notices => _notices;

        /// <summary>候选预制体数量。</summary>
        public int CandidateCount => _entries.Count;

        /// <summary>错误总数。</summary>
        public int ErrorCount => CountIssues(UIPrefabIssueSeverity.Error);

        /// <summary>警告总数。</summary>
        public int WarningCount => CountIssues(UIPrefabIssueSeverity.Warning);

        /// <summary>提示总数。</summary>
        public int InfoCount => CountIssues(UIPrefabIssueSeverity.Info);

        /// <summary>无任何发现的候选数。</summary>
        public int CleanCount
        {
            get
            {
                var count = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (!_entries[i].HasIssues)
                        count++;
                }

                return count;
            }
        }

        /// <summary>
        /// 登记一个候选预制体。
        /// </summary>
        /// <param name="entry">结果；为 null 时忽略。</param>
        public void AddEntry(UIPrefabValidationEntry entry)
        {
            if (entry != null)
                _entries.Add(entry);
        }

        /// <summary>
        /// 登记一条全局通知。
        /// </summary>
        /// <param name="severity">等级。</param>
        /// <param name="message">说明。</param>
        public void AddNotice(UIPrefabIssueSeverity severity, string message)
        {
            _notices.Add(new UIPrefabValidationNotice(severity, message));
        }

        /// <summary>
        /// 按「最严重等级从高到低、同等级按路径升序」排序。
        /// </summary>
        public void SortEntries()
        {
            _entries.Sort((left, right) =>
            {
                var bySeverity = right.MaxSeverity.CompareTo(left.MaxSeverity);
                return bySeverity != 0
                    ? bySeverity
                    : string.Compare(left.AssetPath, right.AssetPath, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// 生成一行汇总文本。
        /// </summary>
        public string BuildSummary()
        {
            var builder = new StringBuilder(96);
            builder.Append("候选 ");
            builder.Append(CandidateCount);
            builder.Append(" 个：错误 ");
            builder.Append(ErrorCount);
            builder.Append("、警告 ");
            builder.Append(WarningCount);
            builder.Append("、提示 ");
            builder.Append(InfoCount);
            builder.Append("；无问题 ");
            builder.Append(CleanCount);
            builder.Append(" 个。");
            return builder.ToString();
        }

        private int CountIssues(UIPrefabIssueSeverity severity)
        {
            var count = 0;
            for (int i = 0; i < _entries.Count; i++)
                count += _entries[i].Count(severity);

            return count;
        }
    }
}
