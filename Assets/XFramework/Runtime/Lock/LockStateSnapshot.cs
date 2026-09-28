namespace XFramework.XLock
{
    /// <summary>
    /// <see cref="LockManager"/> 的状态快照（诊断用）。
    /// <para><b>回答什么问题</b>：现在有多少锁、挂在多少主体上，其中多少**永远不会自动释放**。
    /// <see cref="UnboundSubjectCount"/> 是这套数字里最有诊断价值的一个——它数的是「有锁但没有销毁绑定」
    /// 的主体，也就是 <see cref="LockManager.AutoReleaseOnDestroy"/> 覆盖不到、只能靠调用方显式
    /// <see cref="LockManager.RemoveAllLocks"/> 收口的那一批。</para>
    /// <para><b>怎么取</b>：<see cref="LockManager.GetSnapshot"/>（零分配）；要人读的多行文本用
    /// <see cref="LockManager.DumpState"/>。</para>
    /// <para><b>口径</b>：各值都是此刻的瞬时值，不含历史；<see cref="UnboundSubjectCount"/> 与
    /// <see cref="UnboundSubscribedSubjectCount"/> 在 <see cref="LockManager.AutoReleaseOnDestroy"/> 为
    /// <c>false</c> 时必然等于对应的总数（开关关了，自然一个都没绑）；本类型**不是业务分支的依据**，
    /// 与 <c>XEvent.IEventStream.SubscriptionCount</c> 同属「拉取面」。</para>
    /// </summary>
    public readonly struct LockStateSnapshot
    {
        /// <summary>有锁的主体数（含 <see cref="LockManager.Global"/>）。</summary>
        public readonly int LockedSubjectCount;

        /// <summary>锁总数（所有主体、所有类型的持有者合计）。</summary>
        public readonly int LockCount;

        /// <summary>**有锁但没有销毁绑定**的主体数——这批永远不会自动释放。</summary>
        public readonly int UnboundSubjectCount;

        /// <summary>有订阅的主体数（加锁 / 解锁两张订阅表的键之并集）。</summary>
        public readonly int SubscribedSubjectCount;

        /// <summary>**有订阅但没有销毁绑定**的主体数（孤儿订阅，静态表永久残留）。</summary>
        public readonly int UnboundSubscribedSubjectCount;

        internal LockStateSnapshot(int lockedSubjectCount, int lockCount, int unboundSubjectCount,
            int subscribedSubjectCount, int unboundSubscribedSubjectCount)
        {
            LockedSubjectCount = lockedSubjectCount;
            LockCount = lockCount;
            UnboundSubjectCount = unboundSubjectCount;
            SubscribedSubjectCount = subscribedSubjectCount;
            UnboundSubscribedSubjectCount = unboundSubscribedSubjectCount;
        }

        /// <summary>
        /// 单行摘要，形如 <c>LockStateSnapshot(主体 3, 锁 5, 未绑定 1, 订阅主体 2, 未绑定订阅 1)</c>。
        /// </summary>
        public override string ToString()
            => $"{nameof(LockStateSnapshot)}(主体 {LockedSubjectCount}, 锁 {LockCount}, 未绑定 {UnboundSubjectCount}, " +
               $"订阅主体 {SubscribedSubjectCount}, 未绑定订阅 {UnboundSubscribedSubjectCount})";
    }
}
