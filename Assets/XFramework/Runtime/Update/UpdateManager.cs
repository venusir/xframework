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

        /// <summary>内部调度器单例，负责 LOD 分桶、时间切片等纯调度逻辑。</summary>
        private static UpdateScheduler _scheduler;

        /// <summary>
        /// 应用是否已进入退出流程。
        /// <para>与 <see cref="Clear"/> 的区别：退出是<b>终态</b>（进程即将结束，不再重建），
        /// 手动清空只是重置注册、之后仍可正常使用。</para>
        /// </summary>
        private static bool _shutdown;

        #endregion

        #region Auto Lifecycle

        /// <summary>
        /// 自动初始化更新管理器。
        /// <para>通过 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 保证在任何 MonoBehaviour 之前完成初始化。</para>
        /// </summary>
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        static void AutoInit()
        {
            _scheduler = new UpdateScheduler();
            _shutdown = false;
            Application.quitting += OnQuitting;
        }

        /// <summary>
        /// 应用退出时清理内部状态。
        /// <para>这是<b>不可逆</b>的终态：进程即将结束，此后不再重建调度器。</para>
        /// </summary>
        static void OnQuitting()
        {
            _shutdown = true;
            _scheduler?.Clear();
            _scheduler = null;
            Application.quitting -= OnQuitting;
        }

        #endregion

        #region Public API — 生命周期

        /// <summary>
        /// 是否已初始化（即调度器可用且应用尚未进入退出流程）。
        /// </summary>
        public static bool IsInitialized => _scheduler != null && !_shutdown;

        /// <summary>
        /// 清空全部注册，用于测试隔离或需要重置调度状态的场景。
        /// <para><b>不是终态</b>：清空后仍可继续 <see cref="Register"/> 与 <see cref="Tick"/>。
        /// 进程退出时的清理是另一条路径（<see cref="OnQuitting"/>），那才是不可逆的。</para>
        /// <para><b>为什么改名（原 <c>Destroy</c>）：</b>原实现只清空注册却同时置一个单向闩锁并丢弃调度器，
        /// 使本方法成为「调用一次即永久失效」——而它的文档写的恰恰是「主要用于单元测试隔离」。
        /// 两者自相矛盾：任何 fixture 一旦用它做隔离，同一 play 会话内后续所有 <see cref="Register"/>
        /// 都会静默 no-op（<see cref="Register"/> 开头的 <c>if (_shutdown || _scheduler == null) return;</c>）。
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
            if (_shutdown || _scheduler == null) return;
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
            if (_shutdown || _scheduler == null || node == null) return;
            _scheduler.Register(node, depth, initialLOD);
        }

        /// <summary>
        /// 注销一个可更新对象。
        /// </summary>
        /// <param name="node">要注销的对象。</param>
        public static void Unregister(IUpdateable node)
        {
            if (_shutdown || _scheduler == null || node == null) return;
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
            if (_shutdown || _scheduler == null || node == null) return;
            _scheduler.Enable(node);
        }

        /// <summary>
        /// 禁用指定对象的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnDisable"/>。</para>
        /// </summary>
        /// <param name="node">要禁用的对象。</param>
        public static void Disable(IUpdateable node)
        {
            if (_shutdown || _scheduler == null || node == null) return;
            _scheduler.Disable(node);
        }

        /// <summary>
        /// 检查对象是否处于启用状态。
        /// </summary>
        /// <param name="node">要检查的对象。</param>
        /// <returns>如果对象未被禁用则返回 true。</returns>
        public static bool IsEnabled(IUpdateable node)
        {
            if (_shutdown || _scheduler == null || node == null) return false;
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
            if (_shutdown || _scheduler == null || node == null) return;
            _scheduler.ProcessImmediate(node, deltaTime, time);
        }

        #endregion

        #region Public API — 查询

        /// <summary>
        /// 获取指定 <see cref="UpdateLOD"/> 等级的对象数量。
        /// </summary>
        public static int GetCount(UpdateLOD lod)
        {
            if (_shutdown || _scheduler == null) return 0;
            return _scheduler.GetCount(lod);
        }

        /// <summary>
        /// 获取所有 LOD 等级的对象总数（不含禁用对象）。
        /// </summary>
        public static int TotalCount
        {
            get
            {
                if (_shutdown || _scheduler == null) return 0;
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
                if (_shutdown || _scheduler == null) return 0;
                return _scheduler.DisabledCount;
            }
        }

        #endregion
    }
}