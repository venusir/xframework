using UnityEngine;

namespace XFramework.XUpdate
{
    /// <summary>
    /// 全局更新管理器（静态服务）。
    /// <para>统一管理节点树及静态服务的更新需求，通过内部的 <see cref="UpdateScheduler"/> 提供 LOD 分桶与时间切片调度。</para>
    /// <para>自动生命周期：通过 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 初始化，<see cref="Application.quitting"/> 时自动清理。</para>
    /// <para>每帧通过 <see cref="Tick(float)"/> 驱动，由 <see cref="GameLauncher"/> 在 <c>Update</c> 中调用。</para>
    /// <para>静态服务（非节点树对象）可直接调用 <see cref="Register(IUpdateable, int, UpdateLOD)"/> 注册自身。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>使用示例（静态服务注册）：</b></para>
    /// <code>
    /// // 实现 IUpdateable 接口
    /// public class MyService : IUpdateable
    /// {
    ///     public MyService()
    ///     {
    ///         UpdateManager.Register(this, depth: 0, UpdateLOD.Frame1);
    ///     }
    ///     
    ///     public void OnEnable() { }
    ///     public void OnDisable() { }
    ///     public UpdateLOD OnUpdate(float deltaTime, float time) => UpdateLOD.Frame1;
    /// }
    /// </code>
    /// <para><b>使用示例（节点树节点）：</b></para>
    /// <para>节点树节点实现 <see cref="IUpdateable"/> 后，由 <see cref="UpdateNode"/> 自动注册，无需手动调用本类。</para>
    /// </remarks>
    public static class UpdateManager
    {
        #region Private Fields

        /// <summary>内部调度器单例，负责 LOD 分桶、时间切片等纯调度逻辑。null 表示当前不可用。</summary>
        private static UpdateScheduler _scheduler;

        #endregion

        #region Auto Lifecycle

        /// <summary>
        /// 自动初始化更新管理器（幂等）。
        /// <para><b>为什么两个特性都要挂：</b>编辑器里 <see cref="UnityEditor.InitializeOnLoadMethodAttribute"/>
        /// 只在程序集加载（含重编译引发的域重载）时执行；而在 Project Settings → Editor →
        /// Enter Play Mode Options 里关闭 Reload Domain 后，进入播放<b>不会</b>重新加载程序集，
        /// 该回调不再执行。<see cref="RuntimeInitializeOnLoadMethodAttribute"/> 在编辑器进入播放时
        /// 同样会执行，是关闭域重载时唯一能重置静态状态的时机（Unity 官方推荐的
        /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>，早于首个场景加载）。</para>
        /// <para><b>必须幂等：</b>开启域重载时进入播放会先后触发两者（域重载 → InitializeOnLoadMethod，
        /// 进入播放 → RuntimeInitializeOnLoadMethod）；测试也会显式调用本方法复位门面。</para>
        /// </summary>
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void AutoInit()
        {
            // 只在缺失时重建：第二次调用不能清掉已注册的对象——域重载模式下这一步紧跟在
            // InitializeOnLoadMethod 之后，无条件 new 会给已注册的静态服务（如 InputManager
            // 的帧驱动）换上一份空调度器，表现为「模块自己消失了」
            if (_scheduler == null)
            {
                _scheduler = new UpdateScheduler();
            }

            // 幂等订阅：重复 += 会在自退订之后留下残余订阅；关闭域重载时 Application.quitting
            // 的订阅表跨播放会话存活，残余订阅会逐次累积
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        /// <summary>
        /// 应用退出时清理内部状态：清空注册并释放调度器，下一次 <see cref="AutoInit"/> 会重建。
        /// <para><b>不再有不可逆闩锁：</b>「退出后不复用」由调用时机结构性保证——<see cref="AutoInit"/>
        /// 只在程序集加载与进入播放时触发，进程退出后不会被再调用；而编辑器关闭域重载时，
        /// 下一次进入播放<b>必须</b>能重建，否则整个 Update 模块静默死亡（旧实现正是如此：
        /// <c>_shutdown</c> 置位后 <see cref="Tick"/>/<see cref="Register"/> 等十余处守卫全部
        /// 静默 return，连 <see cref="Clear"/> 都救不回来）。</para>
        /// </summary>
        internal static void OnQuitting()
        {
            _scheduler?.Clear();
            _scheduler = null;
            Application.quitting -= OnQuitting;
        }

        #endregion

        #region Public API — 生命周期

        /// <summary>
        /// 是否已初始化（调度器可用）。
        /// </summary>
        public static bool IsInitialized => _scheduler != null;

        /// <summary>
        /// 清空全部注册，用于测试隔离或需要重置调度状态的场景。
        /// <para><b>不是终态</b>：清空后仍可继续 <see cref="Register"/> 与 <see cref="Tick"/>。
        /// 进程退出时的清理是另一条路径（<see cref="OnQuitting"/>），它丢弃调度器、由下一次
        /// <see cref="AutoInit"/> 重建。</para>
        /// <para><b>为什么改名（原 <c>Destroy</c>）：</b>原实现只清空注册却同时置一个单向闩锁并丢弃调度器，
        /// 使本方法成为「调用一次即永久失效」——而它的文档写的恰恰是「主要用于单元测试隔离」。
        /// 两者自相矛盾：任何 fixture 一旦用它做隔离，同一 play 会话内后续所有 <see cref="Register"/>
        /// 都会静默 no-op（<see cref="Register"/> 开头的守卫直接 return）。
        /// 现对齐 <see cref="XMessage.MessageManager.Clear"/> 与
        /// <see cref="XNode.NodeFactory.ClearAllPools"/> 的既有命名与语义，
        /// 并与「静态门面在 <c>Destroy</c> 后可重新初始化」的框架惯例一致。</para>
        /// </summary>
        public static void Clear()
        {
            _scheduler?.Clear();
        }

        #endregion

        #region Public API — Tick

        /// <summary>
        /// 执行一帧更新。按 <see cref="UpdateLOD"/> 时间切片算法分发更新。
        /// <para>由 <see cref="GameLauncher.Update"/> 每帧调用一次。</para>
        /// </summary>
        /// <param name="time">当前时间（<see cref="Time.time"/>），由外部传入避免重复获取。</param>
        public static void Tick(float time)
        {
            if (_scheduler == null) return;
            _scheduler.Tick(time);
        }

        #endregion

        #region Public API — 注册与注销

        /// <summary>
        /// 注册一个可更新对象。
        /// <para>节点树节点由 <see cref="UpdateNode"/> 自动注册；静态服务可在初始化时手动调用此方法。</para>
        /// </summary>
        /// <param name="node">要注册的对象。</param>
        /// <param name="depth">排序深度，数值越小越先执行。静态服务建议传 0。</param>
        /// <param name="initialLOD">初始 LOD 等级，默认为 <see cref="UpdateLOD.Frame1"/>。</param>
        public static void Register(IUpdateable node, int depth, UpdateLOD initialLOD = UpdateLOD.Frame1)
        {
            if (_scheduler == null || node == null) return;
            _scheduler.Register(node, depth, initialLOD);
        }

        /// <summary>
        /// 注销一个可更新对象。
        /// </summary>
        /// <param name="node">要注销的对象。</param>
        public static void Unregister(IUpdateable node)
        {
            if (_scheduler == null || node == null) return;
            _scheduler.Unregister(node);
        }

        #endregion

        #region Public API — 启用/禁用

        /// <summary>
        /// 启用指定对象的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnEnable"/>。</para>
        /// </summary>
        /// <param name="node">要启用的对象。</param>
        public static void Enable(IUpdateable node)
        {
            if (_scheduler == null || node == null) return;
            _scheduler.Enable(node);
        }

        /// <summary>
        /// 禁用指定对象的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnDisable"/>。</para>
        /// </summary>
        /// <param name="node">要禁用的对象。</param>
        public static void Disable(IUpdateable node)
        {
            if (_scheduler == null || node == null) return;
            _scheduler.Disable(node);
        }

        /// <summary>
        /// 检查对象是否处于启用状态。
        /// </summary>
        /// <param name="node">要检查的对象。</param>
        /// <returns>如果对象未被禁用则返回 true。</returns>
        public static bool IsEnabled(IUpdateable node)
        {
            if (_scheduler == null || node == null) return false;
            return _scheduler.IsEnabled(node);
        }

        #endregion

        #region Public API — 立即处理

        /// <summary>
        /// 立即对指定对象执行一次更新并重新调整 LOD。
        /// <para>用于外部逻辑变化时需要立即响应，不等下一次时间切片。</para>
        /// </summary>
        /// <param name="node">要立即更新的对象。</param>
        /// <param name="deltaTime">传入的时间差。</param>
        /// <param name="time">当前时间（<see cref="Time.time"/>）。</param>
        public static void ProcessImmediate(IUpdateable node, float deltaTime, float time)
        {
            if (_scheduler == null || node == null) return;
            _scheduler.ProcessImmediate(node, deltaTime, time);
        }

        #endregion

        #region Public API — 查询

        /// <summary>
        /// 获取指定 <see cref="UpdateLOD"/> 等级的对象数量。
        /// </summary>
        public static int GetCount(UpdateLOD lod)
        {
            if (_scheduler == null) return 0;
            return _scheduler.GetCount(lod);
        }

        /// <summary>
        /// 获取所有 LOD 等级的对象总数（不含禁用对象）。
        /// </summary>
        public static int TotalCount
        {
            get
            {
                if (_scheduler == null) return 0;
                return _scheduler.TotalCount;
            }
        }

        /// <summary>
        /// 获取禁用对象数量。
        /// </summary>
        public static int DisabledCount
        {
            get
            {
                if (_scheduler == null) return 0;
                return _scheduler.DisabledCount;
            }
        }

        #endregion
    }
}