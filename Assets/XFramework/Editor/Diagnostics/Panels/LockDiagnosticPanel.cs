using XFramework.XDiagnostics;
using XFramework.XLock;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Lock 页签：锁计数快照与按主体展开的完整转储。
    /// <para>回答「玩家为什么一直不能动」——先看 <c>未绑定主体数</c>（那批锁永远不会自动释放），
    /// 再在转储里找具体是哪个主体、哪一类锁。</para>
    /// </summary>
    internal sealed class LockDiagnosticPanel : IDiagnosticPanel
    {
        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Lock";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 80;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            var snapshot = LockManager.GetSnapshot();

            report.Section("锁计数");
            report.KeyValue("有锁的主体数", snapshot.LockedSubjectCount.ToString());
            report.KeyValue("锁总数", snapshot.LockCount.ToString());
            report.KeyValue("未绑定主体数", snapshot.UnboundSubjectCount.ToString());
            report.KeyValue("有订阅的主体数", snapshot.SubscribedSubjectCount.ToString());
            report.KeyValue("未绑定订阅主体数", snapshot.UnboundSubscribedSubjectCount.ToString());

            if (snapshot.UnboundSubjectCount > 0)
            {
                report.Notice(DiagnosticLevel.Warning,
                    "有主体的锁没有销毁绑定：它们不会随对象销毁自动释放，只能靠显式 RemoveAllLocks 收口。");
            }

            report.Section("按主体转储");
            report.Text(LockManager.DumpState());
        }

        #endregion
    }
}
