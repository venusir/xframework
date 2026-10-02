using System.Collections.Generic;
using XFramework.XDiagnostics;
using XFramework.XMessage;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Message 页签：总线统计与按消息类型的规模表。
    /// <para>回答「订阅泄漏了吗」「缓冲驻留了多少」——<c>BufferedChannelCount</c> 归零与否是缓冲内存
    /// 是否释放的信号（口径见 <c>MessageBusStats</c> 的文档：它不等于「持有重放缓存」）。</para>
    /// <para><b>已知限制</b>：键值通道的具体 key 无法枚举（只能按已知 key 查），
    /// 因此本页签给到「类型」粒度。</para>
    /// </summary>
    internal sealed class MessageDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用。</summary>
        private readonly List<MessageTypeStats> _types = new List<MessageTypeStats>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Message";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 70;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            var stats = MessageManager.GetStats();

            report.Section("总线统计");
            report.KeyValue("通道存储表项", stats.ChannelStoreCount.ToString());
            report.KeyValue("存活通道总数", stats.ChannelCount.ToString());
            report.KeyValue("同步订阅", stats.SyncSubscriptionCount.ToString());
            report.KeyValue("异步订阅", stats.AsyncSubscriptionCount.ToString());
            report.KeyValue("缓冲通道", stats.BufferedChannelCount.ToString());
            report.KeyValue("发布次数", stats.PublishCount.ToString());
            report.KeyValue("请求次数", stats.RequestCount.ToString());
            report.KeyValue("请求处理器", stats.RequestHandlerCount.ToString());
            report.KeyValue("全局过滤器", stats.FilterCount.ToString());

            MessageManager.CopyTypeStats(_types);

            report.Section("按消息类型");
            if (_types.Count == 0)
            {
                report.Text("(没有任何消息类型有存活的通道或订阅)");
                return;
            }

            var table = report.BeginTable(null, "类型", "通道", "同步订阅", "异步订阅", "缓冲", "键值通道");
            for (int i = 0; i < _types.Count; i++)
            {
                var type = _types[i];
                table.AddRow(
                    type.MessageType.Name,
                    type.ChannelCount.ToString(),
                    type.SyncSubscriptionCount.ToString(),
                    type.AsyncSubscriptionCount.ToString(),
                    type.BufferedChannelCount.ToString(),
                    type.KeyedChannelCount.ToString());
            }
        }

        #endregion
    }
}
