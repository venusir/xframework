using System.Collections.Generic;
using XFramework.XDiagnostics;
using XFramework.XUpdate;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Update 页签：时间轴状态、档位分布与节点名单。
    /// <para>回答「谁在每帧跑、各在哪个档位、谁被禁用了」——此前只有计数（<c>TotalCount</c> /
    /// <c>GetCount</c>），没有名单。</para>
    /// </summary>
    internal sealed class UpdateDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用。</summary>
        private readonly List<UpdateNodeInfo> _nodes = new List<UpdateNodeInfo>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Update";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 30;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("暂停", UpdateManager.IsPaused ? "是" : "否");
            report.KeyValue("驱动 PlayerLoop", UpdateManager.IsDrivingPlayerLoop ? "是" : "否");
            report.KeyValue("节点总数", UpdateManager.TotalCount.ToString());
            report.KeyValue("已禁用节点", UpdateManager.DisabledCount.ToString());

            CollectTierDistribution(report);
            CollectNodes(report);
        }

        #endregion

        #region Sections

        private static void CollectTierDistribution(IDiagnosticReport report)
        {
            report.Section("档位分布（第 k 档 = 2^k 个节拍格）");

            // UpdateTier.Max 是 Tier7 的别名，故这个循环正好覆盖全部枚举成员
            var table = report.BeginTable(null, "档位", "节点数");
            for (UpdateTier tier = UpdateTier.Tier0; tier <= UpdateTier.Max; tier++)
                table.AddRow(tier.ToString(), UpdateManager.GetCount(tier).ToString());
        }

        private void CollectNodes(IDiagnosticReport report)
        {
            UpdateManager.CopyNodes(_nodes);

            report.Section("节点（含已禁用）");
            if (_nodes.Count == 0)
            {
                report.Text("(没有已注册的节点)");
                return;
            }

            var table = report.BeginTable(null, "类型", "时机", "时间轴", "档位", "状态", "序");
            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                table.AddRow(
                    node.NodeType.Name,
                    node.Timing.ToString(),
                    node.TimeMode.ToString(),
                    node.Tier.ToString(),
                    node.IsEnabled ? "启用" : "禁用",
                    node.Order.ToString());
            }
        }

        #endregion
    }
}
