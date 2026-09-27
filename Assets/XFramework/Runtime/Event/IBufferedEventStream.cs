namespace XFramework.XEvent
{
    /// <summary>
    /// 带缓冲的事件流:新订阅者订阅时立即收到最近一次投递的值(重放先于实时),之后转为实时投递。
    /// <para><b>语义</b>:从未投递过则不重放,直接从实时开始;每个订阅者各自收到重放;重放与实时共用同一
    /// 回调(订阅侧过滤等逻辑由闭包自身表达,两条路径行为一致,异常隔离也一致);
    /// <see cref="IEventStream{T}.OnCompleted"/> 与 <see cref="System.IDisposable.Dispose"/> 都会清空缓存
    /// ——此后的新订阅者不会重放到陈旧值。</para>
    /// </summary>
    /// <typeparam name="T">事件载荷类型。</typeparam>
    public interface IBufferedEventStream<T> : IEventStream<T>
    {
        /// <summary>
        /// 是否持有可重放的值。
        /// <para><b>不变量:不得用它代替「有没有缓冲流」的判断</b>——缓冲流存在但没有值(订阅过缓冲、
        /// 却从未投递过)是常见状态,此时没有任何东西可重放。它回答的是「有没有值」。</para>
        /// <para>无锁读取(与本流的其它状态读取同源取舍):读到 <c>false</c> 而随后被投递时,最多多回收
        /// 一次空容器,无正确性影响。</para>
        /// </summary>
        bool HasCachedValue { get; }
    }
}
