using System.Collections.Generic;
using XFramework.XDiagnostics;
using XFramework.XPool;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Pool 页签：已创建对象池的规模表。
    /// <para>回答「池在不在漏」——<c>活跃</c> 只增不减通常意味着有实例被取走后没归还；
    /// <c>累计</c> 远大于 <c>闲置 + 活跃</c> 说明池被清过。</para>
    /// <para>池是首次 <c>Get&lt;T&gt;()</c> 时惰性创建的，没建过的类型不在表里。</para>
    /// </summary>
    internal sealed class PoolDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用。</summary>
        private readonly List<PoolStats> _stats = new List<PoolStats>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Pool";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 20;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            PoolManager.CopyPoolStats(_stats);

            report.Section("概览");
            report.KeyValue("已创建池数", PoolManager.PoolCount.ToString());

            report.Section("池（按类型）");
            if (_stats.Count == 0)
            {
                report.Text("(还没有任何池被创建——池是首次 Get<T>() 时惰性建的)");
                return;
            }

            var table = report.BeginTable(null, "类型", "闲置", "活跃", "累计");
            for (int i = 0; i < _stats.Count; i++)
            {
                var stats = _stats[i];
                table.AddRow(
                    stats.ObjectType.Name,
                    stats.CountInactive.ToString(),
                    stats.CountActive.ToString(),
                    stats.CountAll.ToString());
            }
        }

        #endregion
    }
}
