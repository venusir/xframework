using System;
using System.Threading;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XTimer
{
    /// <summary>
    /// 定时器管理器外观。提供一次性延时与固定间隔两类定时器，返回可查询、可重开、零分配的可复用句柄。
    /// <para><b>零配置、无需初始化</b>：本模块是纯静态服务，没有可替换的后端，靠
    /// <see cref="AutoInit"/> 自初始化即可用（同 <c>UpdateManager</c> / <c>LockManager</c> / <c>MessageManager</c>）。</para>
    /// <para><b>时间口径完全来自 <c>XUpdate</c></b>：定时器不新建时间模型，截止时刻建在 Update 给节点的
    /// <c>deltaTime</c> 累积出的逻辑时钟上。于是「受不受 <c>timeScale</c> 影响」「暂停时冻不冻」由
    /// <paramref name="timeMode"/> 一个参数决定，取舍口径与 Update 完全一致。</para>
    /// <para><b>它在 <c>UniTask.Delay</c> 之外多给什么</b>：可查询剩余量的句柄、零分配的可复用槽位、
    /// 不随累加漂移的固定间隔、以及按最近截止时间自动升降的 Update 档位（详见模块 README）。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 1. 一次性延时
    /// TimerManager.After(3f, () => Respawn());
    ///
    /// // 2. 固定间隔，句柄可查询、可停
    /// var tick = TimerManager.Every(1f, () => Score++);
    /// float left = tick.Remaining;
    /// tick.Dispose();                       // 不再需要时释放槽位
    ///
    /// // 3. 冷却：句柄留着反复重置
    /// _cooldown ??= TimerManager.Every(8f, OnReady);
    /// _cooldown.Restart();
    ///
    /// // 4. 随对象销毁自动停（零闭包：static lambda + 状态）
    /// TimerManager.After(1.5f, this, static self => self.OnDone(), cancellationToken: destroyCancellationToken);
    /// </code>
    /// </example>
    public static class TimerManager
    {
        #region Static — Core

        /// <summary>
        /// 当前核心。由 <see cref="AutoInit"/> 复位为 <c>null</c>，此处按需重建——静态服务的懒创建点只有这一处。
        /// </summary>
        private static TimerCore _core;

        /// <summary>取当前核心；<see cref="AutoInit"/> 复位后按需重建。</summary>
        private static TimerCore Core => _core ??= new TimerCore();

        #endregion

        #region Public API — Create

        /// <summary>
        /// 创建一个一次性定时器：<paramref name="delay"/> 秒后回调一次，随后槽位自动释放、句柄失效。
        /// <para><b>首拍不是立刻</b>：<c>After(0f, ...)</c> 的含义是「下一拍触发」，且回调保证发生在
        /// 本方法<b>返回句柄之后</b>——不会出现「句柄还没拿到就已经跑完了」。</para>
        /// <para>精度由所属时间轴与 Update 的档位共同决定：截止时刻前至少还有 8 次派发机会，
        /// 最坏相对超时不超过间隔的 12.5%（详见模块 README 的「档位选择」）。</para>
        /// </summary>
        /// <param name="delay">延时秒数，必须 <c>&gt;= 0</c> 且有限。</param>
        /// <param name="callback">到期回调，不可为 null。<b>回调抛出的异常会被本模块收口</b>：
        /// 记一条 <c>[Timer]</c> 错误日志并停掉该定时器，其余定时器与整个调度不受影响。</param>
        /// <param name="timeMode">时间轴，默认为 <see cref="UpdateTimeMode.Scaled"/>（受 <c>timeScale</c> 影响、
        /// 暂停时冻结）。需要「暂停期间仍计时」请传 <see cref="UpdateTimeMode.Unscaled"/>。</param>
        /// <param name="cancellationToken">取消令牌。令牌被取消即自动停表并回收槽位；可直接传 MonoBehaviour 的
        /// <c>destroyCancellationToken</c> 让定时器随对象销毁而消失。<b>不传时整条路径零额外分配。</b></param>
        /// <returns>定时器句柄；创建时令牌已被取消时返回 <c>default(TimerHandle)</c>（并记一条告警）。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> 为负/NaN/无穷，
        /// 或 <paramref name="timeMode"/> 不是有效枚举值时抛出。</exception>
        public static TimerHandle After(float delay, Action callback,
            UpdateTimeMode timeMode = UpdateTimeMode.Scaled,
            CancellationToken cancellationToken = default)
        {
            return Core.Create(delay, callback, timeMode, cancellationToken, repeating: false);
        }

        /// <summary>
        /// 创建一个一次性定时器，并把 <paramref name="state"/> 原样回传给回调。
        /// <para><b>本重载是为零分配而存在的</b>：配合 <c>static</c> lambda 使用时不会为捕获变量生成闭包。
        /// 状态随槽位存储（按类型分表），<b>值类型状态也不会装箱</b>。</para>
        /// </summary>
        /// <typeparam name="TState">状态类型。</typeparam>
        /// <param name="delay">延时秒数，必须 <c>&gt;= 0</c> 且有限。</param>
        /// <param name="callback">到期回调，不可为 null。</param>
        /// <param name="state">回调收到的状态。</param>
        /// <param name="timeMode">时间轴，默认为 <see cref="UpdateTimeMode.Scaled"/>。</param>
        /// <param name="cancellationToken">取消令牌；不传时零额外分配。</param>
        /// <returns>定时器句柄；创建时令牌已被取消时返回 <c>default(TimerHandle)</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> 为负/NaN/无穷，
        /// 或 <paramref name="timeMode"/> 不是有效枚举值时抛出。</exception>
        public static TimerHandle After<TState>(float delay, Action<TState> callback, TState state,
            UpdateTimeMode timeMode = UpdateTimeMode.Scaled,
            CancellationToken cancellationToken = default)
        {
            return Core.Create(delay, callback, state, timeMode, cancellationToken, repeating: false);
        }

        /// <summary>
        /// 创建一个固定间隔定时器：每隔 <paramref name="interval"/> 秒回调一次，直到
        /// <see cref="TimerHandle.Stop"/> / <see cref="TimerHandle.Dispose"/> / 令牌取消。
        /// <para><b>首拍 = 隔一个 interval</b>，不是立刻（需要立刻做一次请自己在创建处先调一次）。</para>
        /// <para><b>不漂移</b>：第 k 拍的时刻恒为「锚点 + k × interval」，不是逐拍累加 <c>deltaTime</c>，
        /// 因此长期平均间隔精确、相位不随卡顿走样。</para>
        /// <para><b>一帧跨多拍时跳过、不补发</b>：卡顿 1 秒不会让 0.1 秒的定时器连发 10 次——
        /// 这与 Update 的「宁可延长也不突发」同一条取舍。</para>
        /// </summary>
        /// <param name="interval">间隔秒数，必须 <c>&gt; 0</c> 且有限。</param>
        /// <param name="callback">每拍回调，不可为 null。异常处置同
        /// <see cref="After(float, Action, UpdateTimeMode, CancellationToken)"/>（停掉该定时器）。</param>
        /// <param name="timeMode">时间轴，默认为 <see cref="UpdateTimeMode.Scaled"/>。</param>
        /// <param name="cancellationToken">取消令牌；不传时零额外分配。</param>
        /// <returns>定时器句柄；创建时令牌已被取消时返回 <c>default(TimerHandle)</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> 非正/NaN/无穷，
        /// 或 <paramref name="timeMode"/> 不是有效枚举值时抛出。</exception>
        public static TimerHandle Every(float interval, Action callback,
            UpdateTimeMode timeMode = UpdateTimeMode.Scaled,
            CancellationToken cancellationToken = default)
        {
            return Core.Create(interval, callback, timeMode, cancellationToken, repeating: true);
        }

        /// <summary>
        /// 创建一个固定间隔定时器，并把 <paramref name="state"/> 原样回传给每次回调。
        /// <para>零分配语义同 <see cref="After{TState}(float, Action{TState}, TState, UpdateTimeMode, CancellationToken)"/>。</para>
        /// </summary>
        /// <typeparam name="TState">状态类型。</typeparam>
        /// <param name="interval">间隔秒数，必须 <c>&gt; 0</c> 且有限。</param>
        /// <param name="callback">每拍回调，不可为 null。</param>
        /// <param name="state">回调收到的状态。</param>
        /// <param name="timeMode">时间轴，默认为 <see cref="UpdateTimeMode.Scaled"/>。</param>
        /// <param name="cancellationToken">取消令牌；不传时零额外分配。</param>
        /// <returns>定时器句柄；创建时令牌已被取消时返回 <c>default(TimerHandle)</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> 非正/NaN/无穷，
        /// 或 <paramref name="timeMode"/> 不是有效枚举值时抛出。</exception>
        public static TimerHandle Every<TState>(float interval, Action<TState> callback, TState state,
            UpdateTimeMode timeMode = UpdateTimeMode.Scaled,
            CancellationToken cancellationToken = default)
        {
            return Core.Create(interval, callback, state, timeMode, cancellationToken, repeating: true);
        }

        #endregion

        #region Public API — Control

        /// <summary>
        /// 取消全部定时器（两轴合计，含已 <see cref="TimerHandle.Stop"/> 但未释放的），并让内部驱动器退出调度。
        /// <para>本方法是<b>彻底复位</b>：所有槽位被释放、所有句柄（含已停止的）失效、逻辑时钟归零。
        /// 之后仍可继续创建定时器。</para>
        /// <para><c>UpdateManager.Clear()</c> 会连带摘掉本模块的驱动器（它清的是全局注册表）。若在它之后发现
        /// 定时器不再触发，调用本方法即可让模块重新同步。</para>
        /// </summary>
        /// <returns>被取消的定时器数量（不含已停止的）。</returns>
        public static int CancelAll()
        {
            return Core.CancelAll();
        }

        /// <summary>
        /// 当前正在计时的定时器数量（两轴合计）。
        /// <para><b>不含</b>已 <see cref="TimerHandle.Stop"/> 的与已释放的；令牌已取消的定时器会在
        /// <b>下一拍扫尾</b>时从这里扣除，而句柄的 <see cref="TimerHandle.IsActive"/> 则是立刻为 <c>false</c>——
        /// 两者最坏相差一个派发周期。</para>
        /// </summary>
        public static int ActiveCount => Core.ActiveCount;

        #endregion

        #region Internal — Handle Forwarding

        /// <summary>
        /// 句柄的查询与控制入口。
        /// <para><b>门面不把这一组做成公开成员</b>：句柄本身就是公开面，同一语义在门面再暴露一遍就成了
        /// 两处真相（<c>LockManager</c> 的 <c>LockHandle.Dispose</c> 同理不转发到同名静态方法）。</para>
        /// </summary>
        /// <param name="handle">定时器句柄。</param>
        internal static bool IsActive(in TimerHandle handle) => Core.IsActive(in handle);

        /// <summary>距下一拍触发的剩余秒数；无效/已停/已取消时为 <c>0</c>。</summary>
        /// <param name="handle">定时器句柄。</param>
        internal static float Remaining(in TimerHandle handle) => Core.Remaining(in handle);

        /// <summary>停表但保留槽位。</summary>
        /// <param name="handle">定时器句柄。</param>
        internal static bool Stop(in TimerHandle handle) => Core.Stop(in handle);

        /// <summary>用原时长重新起算。</summary>
        /// <param name="handle">定时器句柄。</param>
        internal static bool Restart(in TimerHandle handle) => Core.Restart(in handle);

        /// <summary>停表并释放槽位。</summary>
        /// <param name="handle">定时器句柄。</param>
        internal static void Release(in TimerHandle handle) => Core.Release(in handle);

        /// <summary>测试缝：指定时间轴的驱动器当前是否已注册进 Update。</summary>
        /// <param name="timeMode">时间轴。</param>
        internal static bool IsTickerRegistered(UpdateTimeMode timeMode)
        {
            return Core.TickerOf((int)timeMode).IsRegistered;
        }

        #endregion

        #region Internal — Auto Lifecycle

        /// <summary>
        /// 自动初始化：复位核心并补挂「退出时清理」的订阅，无需外部调用（幂等）。
        /// <para><b>两个特性都要挂：</b>编辑器里 <see cref="UnityEditor.InitializeOnLoadMethodAttribute"/>
        /// 只在程序集加载（含重编译引发的域重载）时执行；而在 Project Settings → Editor →
        /// Enter Play Mode Options 里关闭 Reload Domain 后，进入播放<b>不会</b>重新加载程序集，该回调
        /// 不再执行。<see cref="RuntimeInitializeOnLoadMethodAttribute"/> 在进入播放时同样会执行，
        /// 是关闭域重载时唯一能补挂它的时机。</para>
        /// <para><b>订阅必须幂等：</b>开启域重载时进入播放会先后触发两者；关闭域重载时
        /// <c>Application.quitting</c> 的订阅表还会跨播放会话存活，重复 <c>+=</c> 会逐次累积。
        /// 下面用「先 <c>-=</c> 再 <c>+=</c>」保证恰好一条——**因此 <see cref="OnQuitting"/> 不自退订**：
        /// 那行的正确性完全依赖这里的 <c>-=</c> 先执行，删掉它这条保证就没了。</para>
        /// <para><b>档位取 <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/></b>：本方法做的是
        /// <b>复位</b>（丢弃上一会话残留的定时器与句柄），必须早于任何可能创建定时器的初始化——
        /// 与 <c>UpdateManager.AutoInit</c> 同档。想改档位请同步更新
        /// <c>Tests/Runtime/Architecture/AutoInitTests</c> 的族清单，否则守卫会红。</para>
        /// </summary>
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void AutoInit()
        {
            // 丢弃上一会话的核心：关闭域重载时静态字段跨播放会话存活，不清就会带着早已失效的槽位与
            // 句柄进入新一轮播放。先 CancelAll 让旧核心把两个驱动器从调度器里摘干净，再置空——
            // 只剩置空的话，旧驱动器会一直挂在 Update 上（对象还能被调度器强引用着，跑一个空内核）
            ResetCore();

            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        /// <summary>
        /// 退出清理：停净全部定时器并丢弃核心。
        /// <para>不在这里退订——正确性依赖 <see cref="AutoInit"/> 里的先 <c>-=</c> 再 <c>+=</c>。</para>
        /// </summary>
        private static void OnQuitting()
        {
            ResetCore();
        }

        /// <summary>停净旧核心并置空，下一次使用按需重建。</summary>
        private static void ResetCore()
        {
            TimerCore core = _core;
            _core = null;
            core?.CancelAll();
        }

        #endregion
    }
}
