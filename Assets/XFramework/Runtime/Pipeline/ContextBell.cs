namespace XFramework.XPipeline
{
    /// <summary>
    /// 上下文写入门铃的接收方(内部)。门铃负责重入折叠与迟写防护,接收方只管聚合本身。
    /// </summary>
    internal interface IContextBellSink
    {
        /// <summary>执行一次聚合(重入折叠、迟写防护均由门铃负责,本方法不做这两件事)。</summary>
        void AggregateOnce();
    }

    /// <summary>
    /// 上下文写入门铃(内部):重入折叠 + 迟写防护,管线实现与容器聚合器共用。
    /// <para>两个接收方原先各持一份逐行同构的实现(与本模块 <see cref="ContextAggregation"/> 抽取前
    /// 的情形一样),且差异正好落在缺陷上:管线侧漏了迟写防护,终局事件之后仍会被回写触发出一次广播。
    /// 收敛为单一实现后,两侧行为由构造保证一致。</para>
    /// <para>折叠的边界要说清:一次外部写入最多触发两次聚合(本次 + 收尾补一次),收尾期间的重入写
    /// 同样被折叠——因此递归深度恒为 2,不随订阅者回写次数增长。刻意不写成 while 排空:订阅者若在
    /// 每次广播都回写,while 会变成死循环,不管它则会栈溢出——宁可少广播一次。</para>
    /// </summary>
    internal sealed class ContextBell
    {
        #region Public API

        /// <summary>是否已关闸(沉降)。关闸后 <see cref="Ring"/> 直接忽略写入。</summary>
        internal bool IsSettled => _settled;

        /// <summary>复位(每次运行 / 每次容器执行开始),三项状态一并清空。</summary>
        internal void Reset()
        {
            _aggregating = false;
            _dirty = false;
            _settled = false;
        }

        /// <summary>关闸:此后写入不再触发聚合(迟写防护)。</summary>
        internal void Settle()
        {
            _settled = true;
        }

        /// <summary>
        /// 响一次门铃。聚合在写入栈内同步发生,广播/转发中再次写入会递归——用
        /// <see cref="_aggregating"/> + <see cref="_dirty"/> 把重入写折叠为收尾补聚合一次。
        /// </summary>
        internal void Ring(IContextBellSink sink)
        {
            if (_settled) return;

            if (_aggregating)
            {
                _dirty = true;
                return;
            }

            _aggregating = true;
            try
            {
                sink.AggregateOnce();

                // 收尾补聚合在守卫内:重入写在收尾期间同样被折叠,递归深度因此恒为 2
                if (_dirty)
                {
                    _dirty = false;
                    sink.AggregateOnce();
                }
            }
            finally
            {
                _aggregating = false;
            }
        }

        #endregion

        #region Private Fields

        /// <summary>聚合重入保护:聚合(含收尾补聚合)期间的重入写只置脏。</summary>
        bool _aggregating;

        /// <summary>收尾补聚合标记:重入写把这一轮标脏,由外层收尾消费一次。</summary>
        bool _dirty;

        /// <summary>已关闸标志(迟写防护:关闸后写入经门铃直接忽略)。</summary>
        bool _settled;

        #endregion
    }
}
