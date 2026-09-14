using UnityEngine;
using UnityEngine.LowLevel;

namespace XFramework.XUpdate
{
    /// <summary>
    /// 全局更新管理器（静态服务）。
    /// <para>统一管理节点树及静态服务的更新需求，通过内部的 <see cref="UpdateScheduler"/> 提供 LOD 分桶与时间切片调度。</para>
    /// <para>自动生命周期：通过 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 初始化，<see cref="Application.quitting"/> 时自动清理。</para>
    /// <para>每帧由注入到 PlayerLoop 的驱动自动推进（见 <see cref="IsDrivingPlayerLoop"/>），
    /// 不依赖场景中存在任何 MonoBehaviour；<see cref="Tick(float)"/> 保留供手动驱动与测试使用。</para>
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
    ///         UpdateManager.Register(this, depth: 0, UpdateLOD.Tier0);
    ///     }
    ///     
    ///     public void OnEnable() { }
    ///     public void OnDisable() { }
    ///     public UpdateLOD OnUpdate(float deltaTime, float time) => UpdateLOD.Tier0;
    /// }
    /// </code>
    /// <para><b>使用示例（节点树节点）：</b></para>
    /// <para>节点树节点实现 <see cref="IUpdateable"/> 后，由 <see cref="UpdateNode"/> 自动注册，无需手动调用本类。</para>
    /// </remarks>
    public static class UpdateManager
    {
        #region Private Fields

        /// <summary>Update 时机在 PlayerLoop 中的承载子系统名，驱动注入到它的子列表末尾。</summary>
        private const string UpdateDriverTargetSystemName = "ScriptRunBehaviourUpdate";

        /// <summary>LateUpdate 时机的承载子系统名。</summary>
        private const string LateUpdateDriverTargetSystemName = "ScriptRunBehaviourLateUpdate";

        /// <summary>FixedUpdate 时机的承载子系统名。</summary>
        private const string FixedUpdateDriverTargetSystemName = "ScriptRunBehaviourFixedUpdate";

        /// <summary>
        /// 各时机的调度器，下标即 <see cref="UpdateTiming"/>。null 表示当前不可用。
        /// <para>每时机一套独立实例（各自的桶、切片节拍、相位、暂停状态），因为它们由 PlayerLoop
        /// 的不同阶段驱动、节奏互不相干——共用一个实例会让两种时机的切片相位互相干扰。</para>
        /// </summary>
        private static UpdateScheduler[] _schedulers;

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
            if (_schedulers == null)
            {
                _schedulers = new[]
                {
                    new UpdateScheduler(UpdateTiming.Update),
                    new UpdateScheduler(UpdateTiming.LateUpdate),
                    new UpdateScheduler(UpdateTiming.FixedUpdate),
                };
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
            ClearAllSchedulers();
            _schedulers = null;
            Application.quitting -= OnQuitting;
        }

        #endregion

        #region PlayerLoop 驱动

        /// <summary>
        /// 是否允许 PlayerLoop 自动驱动。默认开启。
        /// <para><b>仅供测试关闭</b>：PlayMode 用例在 <c>yield</c> 期间会被自动驱动派发，
        /// 精确计数断言会被打乱。经 <c>InternalsVisibleTo</c> 访问，与框架其它测试钩子
        /// （<c>SetInstance</c> / <c>Initialize(实例)</c>）同惯例。</para>
        /// </summary>
        internal static bool AutoDriveEnabled { get; set; } = true;

        /// <summary>
        /// 驱动委托实例。只创建一次——<see cref="PlayerLoopSystem.updateDelegate"/> 每帧被调用，
        /// 委托本身不能每帧新建。
        /// </summary>
        private static readonly PlayerLoopSystem.UpdateFunction UpdateDriverDelegate = DriveUpdate;

        /// <summary>LateUpdate 时机的驱动委托实例。</summary>
        private static readonly PlayerLoopSystem.UpdateFunction LateUpdateDriverDelegate = DriveLateUpdate;

        /// <summary>FixedUpdate 时机的驱动委托实例。</summary>
        private static readonly PlayerLoopSystem.UpdateFunction FixedUpdateDriverDelegate = DriveFixedUpdate;

        /// <summary>
        /// 自动驱动是否已生效：当前 PlayerLoop 中是否含本框架的<b>三个</b>驱动系统
        /// （Update / LateUpdate / FixedUpdate）。任一缺失即为 false——注入失败时不会有任何东西派发。
        /// </summary>
        public static bool IsDrivingPlayerLoop
        {
            get
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                return ContainsDriver(loop, UpdateDriverDelegate)
                       && ContainsDriver(loop, LateUpdateDriverDelegate)
                       && ContainsDriver(loop, FixedUpdateDriverDelegate);
            }
        }

        /// <summary>
        /// 把驱动系统注入 PlayerLoop（幂等）。
        /// <para><b>只插入、不替换</b>：必须基于 <see cref="PlayerLoop.GetCurrentPlayerLoop"/>，
        /// 不能用 <c>GetDefaultPlayerLoop</c>——框架依赖 UniTask，而它正是靠注入 PlayerLoop 工作的，
        /// 用默认 loop 会连同其它插件的注入一起冲掉。</para>
        /// <para>注入点是 <c>Update.ScriptRunBehaviourUpdate</c> 子系统列表的末尾，语义等价于
        /// 「所有 MonoBehaviour.Update 之后」，与原先由 <see cref="GameLauncher"/> 在 Update 里
        /// 驱动的时机一致。</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        internal static void AutoInjectDriver()
        {
            TryInjectDriver();
        }

        /// <summary>
        /// 注入驱动；已注入则跳过。
        /// <para>判断依据是「当前 loop 里有没有我们的系统」而不是布尔标志：关闭域重载时静态字段
        /// 跨播放会话存活，而 PlayerLoop 会在进入播放时重建——标志会失真，结构性检查不会。</para>
        /// </summary>
        /// <returns>注入是否已生效。</returns>
        internal static bool TryInjectDriver()
        {
            if (!Application.isPlaying) return false;

            var loop = PlayerLoop.GetCurrentPlayerLoop();

            bool inserted = false;
            bool ok = EnsureDriverSystem(ref loop, UpdateDriverTargetSystemName, UpdateDriverDelegate, ref inserted);
            ok &= EnsureDriverSystem(ref loop, LateUpdateDriverTargetSystemName, LateUpdateDriverDelegate, ref inserted);
            ok &= EnsureDriverSystem(ref loop, FixedUpdateDriverTargetSystemName, FixedUpdateDriverDelegate, ref inserted);

            if (inserted)
            {
                PlayerLoop.SetPlayerLoop(loop);
            }

            if (!ok)
            {
                // 注入失败必须留痕：此时没有任何东西会每帧调用 Tick，而门面本身是宽容语义、
                // 不会报错——不打日志的话表现为「所有 IUpdateable 静止」
                Debug.LogWarning(
                    "[Update] 未在 PlayerLoop 中找到驱动目标子系统（ScriptRunBehaviourUpdate / " +
                    "ScriptRunBehaviourLateUpdate），自动驱动未生效；请自行每帧调用 UpdateManager.Tick。");
            }

            return ok;
        }

        /// <summary>
        /// 确保某个时机的驱动系统已在 loop 中；缺失则插入。已被插入时通过
        /// <paramref name="inserted"/> 回报（调用方据此决定是否写回 PlayerLoop）。
        /// </summary>
        private static bool EnsureDriverSystem(ref PlayerLoopSystem loop, string targetSystemName,
            PlayerLoopSystem.UpdateFunction driver, ref bool inserted)
        {
            if (ContainsDriver(loop, driver)) return true;

            var injection = new PlayerLoopSystem
            {
                type = typeof(UpdateManager),
                updateDelegate = driver,
            };

            if (!TryAppendToDriverTarget(ref loop, targetSystemName, injection))
            {
                return false;
            }

            inserted = true;
            return true;
        }

        /// <summary>
        /// 每帧驱动入口（Update 时机）。<b>零分配</b>：<see cref="UpdateClock"/> 是栈上结构体。
        /// </summary>
        private static void DriveUpdate()
        {
            if (!AutoDriveEnabled) return;

            _schedulers?[(int)UpdateTiming.Update]?.Tick(BuildClock());
        }

        /// <summary>
        /// 每帧驱动入口（LateUpdate 时机）。
        /// </summary>
        private static void DriveLateUpdate()
        {
            if (!AutoDriveEnabled) return;

            _schedulers?[(int)UpdateTiming.LateUpdate]?.Tick(BuildClock());
        }

        /// <summary>
        /// 每个固定步驱动入口。<b>时间基准是 <see cref="Time.fixedTime"/> 而不是每帧变化的
        /// <see cref="Time.time"/>：<see cref="UpdateLOD"/> 的档位在这里是「每 2^k 个固定步」，
        /// 该轴逐步推进一格、不参与变步长轴的 60Hz 节拍（固定步长本就等长，没有漂移可修）。</b>
        /// </summary>
        private static void DriveFixedUpdate()
        {
            if (!AutoDriveEnabled) return;

            _schedulers?[(int)UpdateTiming.FixedUpdate]?.Tick(BuildFixedClock());
        }

        /// <summary>
        /// 构造本帧时钟：两个时间源 + 逻辑时间是否冻结（<c>timeScale &lt;= 0</c>）。
        /// </summary>
        private static UpdateClock BuildClock()
        {
            return new UpdateClock(Time.time, Time.unscaledTime, Time.timeScale <= 0f);
        }

        /// <summary>
        /// 构造固定步时钟：两条轴都用 <see cref="Time.fixedTime"/>（固定步没有「墙钟轴」的概念）。
        /// </summary>
        private static UpdateClock BuildFixedClock()
        {
            return new UpdateClock(Time.fixedTime, Time.fixedTime, Time.timeScale <= 0f);
        }

        /// <summary>
        /// 在 PlayerLoop 树中查找指定名字的子系统，并把注入追加到它的子系统列表末尾。
        /// <para>按类型<b>名</b>查找而不是按 Type 引用：跨 Unity 版本更稳，也避免引用嵌套类型。</para>
        /// </summary>
        private static bool TryAppendToDriverTarget(ref PlayerLoopSystem system, string targetSystemName,
            PlayerLoopSystem injection)
        {
            if (system.type != null && system.type.Name == targetSystemName)
            {
                var subs = system.subSystemList;
                var grown = new PlayerLoopSystem[(subs?.Length ?? 0) + 1];
                if (subs != null)
                {
                    System.Array.Copy(subs, grown, subs.Length);
                }
                grown[grown.Length - 1] = injection;
                system.subSystemList = grown;
                return true;
            }

            if (system.subSystemList == null) return false;

            for (int i = 0; i < system.subSystemList.Length; i++)
            {
                if (TryAppendToDriverTarget(ref system.subSystemList[i], targetSystemName, injection)) return true;
            }

            return false;
        }

        /// <summary>
        /// 当前 PlayerLoop 中是否已含指定的驱动系统。
        /// </summary>
        private static bool ContainsDriver(in PlayerLoopSystem system, PlayerLoopSystem.UpdateFunction driver)
        {
            if (system.updateDelegate == driver) return true;

            if (system.subSystemList == null) return false;

            for (int i = 0; i < system.subSystemList.Length; i++)
            {
                if (ContainsDriver(system.subSystemList[i], driver)) return true;
            }

            return false;
        }

        #endregion

        #region Public API — 生命周期

        /// <summary>
        /// 是否已初始化（调度器可用）。
        /// </summary>
        public static bool IsInitialized => _schedulers != null;

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
            ClearAllSchedulers();
        }

        /// <summary>
        /// 清空全部时机的调度器。
        /// </summary>
        private static void ClearAllSchedulers()
        {
            if (_schedulers == null) return;

            for (int i = 0; i < _schedulers.Length; i++)
            {
                _schedulers[i].Clear();
            }
        }

        /// <summary>
        /// 取指定时机的调度器；未初始化时返回 null。
        /// </summary>
        private static UpdateScheduler SchedulerOf(UpdateTiming timing)
        {
            return _schedulers?[(int)timing];
        }

        #endregion

        #region Public API — Tick

        /// <summary>
        /// 执行一帧更新（Update 与 LateUpdate 两个变步长时机）。按 <see cref="UpdateLOD"/> 时间切片算法分发。
        /// <para><b>生产路径不需要调用本方法</b>：驱动已注入 PlayerLoop。<see cref="IsDrivingPlayerLoop"/>
        /// 为 false 时才需要自行每帧调用（注入生效时再手动调用会导致同一帧派发两次）。</para>
        /// <para>本重载用同一个时刻驱动两条时间轴（<see cref="UpdateTimeMode"/>）；
        /// 需要墙钟轴独立走得请用 <see cref="Tick(UpdateClock)"/>。</para>
        /// </summary>
        /// <param name="time">当前时间（<see cref="Time.time"/>），由外部传入避免重复获取。</param>
        public static void Tick(float time)
        {
            Tick(new UpdateClock(time, time));
        }

        /// <summary>
        /// 执行一帧更新（Update 与 LateUpdate 两个变步长时机），两条时间轴各用自己的时刻。
        /// <para>驱动方构造时钟时填 <see cref="Time.time"/> 与 <see cref="Time.unscaledTime"/>，
        /// 调度器本身不去读 <see cref="Time"/>，因此可被单测精确驱动。</para>
        /// </summary>
        /// <param name="clock">本帧的时间基。</param>
        public static void Tick(in UpdateClock clock)
        {
            if (_schedulers == null) return;

            // 只驱动变步长时机：固定步长时机由 FixedUpdate 阶段的驱动按 fixedTime 推进，
            // 用变步长时钟驱动它会让「每 N 个固定步」的语义失真（见 TickFixed）
            SchedulerOf(UpdateTiming.Update)?.Tick(clock);
            SchedulerOf(UpdateTiming.LateUpdate)?.Tick(clock);
        }

        /// <summary>
        /// 手动推进一次固定步长时机。
        /// <para>与 <see cref="Tick(UpdateClock)"/> 分开而不是合并：固定步长的时间基准是
        /// <see cref="Time.fixedTime"/>，<see cref="UpdateLOD"/> 的档位在这里是「每 2^k 个固定步」。</para>
        /// <para>生产路径不需要调用本方法（驱动已注入 <c>FixedUpdate</c> 阶段）；供手动驱动与测试使用。</para>
        /// </summary>
        /// <param name="fixedTime">当前固定步时间（<see cref="Time.fixedTime"/>）。</param>
        public static void TickFixed(float fixedTime)
        {
            SchedulerOf(UpdateTiming.FixedUpdate)?.Tick(fixedTime);
        }

        #endregion

        #region Public API — 暂停

        /// <summary>
        /// 暂停<b>逻辑时间轴</b>的派发。
        /// <para>与 <c>Time.timeScale = 0</c> 的区别：本方法不改动 Unity 时间，供「暂停但不希望
        /// UI 动画、手柄振动等跟着慢下来」的场景使用；用 <c>timeScale = 0</c> 暂停同样会让逻辑轴
        /// 冻结（驱动把它填进 <see cref="UpdateClock.IsPaused"/>）。两条路径都不影响
        /// <see cref="UpdateTimeMode.Unscaled"/> 轴上的对象。</para>
        /// <para><b>恢复时不追赶</b>：<see cref="Resume"/> 会把时间基准重锚，恢复后的第一帧
        /// delta 为 0，而不是把整段暂停时长一次性补完。确有追赶需求的逻辑请在节点内自行累加。</para>
        /// </summary>
        public static void Pause()
        {
            if (_schedulers == null) return;

            for (int i = 0; i < _schedulers.Length; i++)
            {
                _schedulers[i].Pause();
            }
        }

        /// <summary>
        /// 恢复逻辑时间轴的派发（不追赶，见 <see cref="Pause"/>）。
        /// </summary>
        public static void Resume()
        {
            if (_schedulers == null) return;

            for (int i = 0; i < _schedulers.Length; i++)
            {
                _schedulers[i].Resume();
            }
        }

        /// <summary>
        /// 逻辑轴当前是否已暂停：本门面的 <see cref="Pause"/> 开关，或 <c>Time.timeScale &lt;= 0</c>。
        /// </summary>
        public static bool IsPaused
        {
            get
            {
                if (Time.timeScale <= 0f) return true;
                if (_schedulers == null) return false;

                for (int i = 0; i < _schedulers.Length; i++)
                {
                    if (_schedulers[i].IsPaused) return true;
                }
                return false;
            }
        }

        #endregion

        #region Public API — 注册与注销

        /// <summary>
        /// 注册一个 <see cref="UpdateTiming.Update"/> 时机的可更新对象。
        /// <para>节点树节点由 <see cref="UpdateNode"/> 自动注册；静态服务可在初始化时手动调用此方法。</para>
        /// </summary>
        /// <param name="node">要注册的对象。</param>
        /// <param name="depth">排序深度，数值越小越先执行。静态服务建议传 0。</param>
        /// <param name="initialLOD">初始 LOD 等级，默认为 <see cref="UpdateLOD.Tier0"/>。</param>
        /// <param name="timeMode">时间轴，默认为 <see cref="UpdateTimeMode.Scaled"/>。
        /// 需要「暂停期间仍运行」的逻辑（暂停菜单、UI 动画、手柄振动到期）请用
        /// <see cref="UpdateTimeMode.Unscaled"/>。</param>
        public static void Register(IUpdateable node, int depth, UpdateLOD initialLOD = UpdateLOD.Tier0,
            UpdateTimeMode timeMode = UpdateTimeMode.Scaled)
        {
            if (node == null) return;
            SchedulerOf(UpdateTiming.Update)?.Register(node, depth, initialLOD, timeMode);
        }

        /// <summary>
        /// 注册一个 <see cref="UpdateTiming.LateUpdate"/> 时机的可更新对象。
        /// <para>与 <see cref="Register(IUpdateable, int, UpdateLOD, UpdateTimeMode)"/> 分开而不是共用一个
        /// <c>timing</c> 参数：那样参数类型只能退化成 <see cref="IUpdateLifecycle"/>，
        /// 「把对象注册进它没实现的时机」要到派发时才炸。</para>
        /// </summary>
        /// <param name="node">要注册的对象。</param>
        /// <param name="depth">排序深度，数值越小越先执行。静态服务建议传 0。</param>
        /// <param name="initialLOD">初始 LOD 等级，默认为 <see cref="UpdateLOD.Tier0"/>。</param>
        /// <param name="timeMode">时间轴，默认为 <see cref="UpdateTimeMode.Scaled"/>。</param>
        public static void RegisterLate(ILateUpdateable node, int depth, UpdateLOD initialLOD = UpdateLOD.Tier0,
            UpdateTimeMode timeMode = UpdateTimeMode.Scaled)
        {
            if (node == null) return;
            SchedulerOf(UpdateTiming.LateUpdate)?.Register(node, depth, initialLOD, timeMode);
        }

        /// <summary>
        /// 注册一个 <see cref="UpdateTiming.FixedUpdate"/> 时机的可更新对象。
        /// <para>与另外两个时机不同，这里<b>没有时间轴参数</b>：Unity 的固定步长本就随
        /// <c>timeScale</c> 停摆，不存在「暂停期间仍运行的固定步」这种语义，
        /// 因此不需要（也不该假装能）选轴。</para>
        /// </summary>
        /// <param name="node">要注册的对象。</param>
        /// <param name="depth">排序深度，数值越小越先执行。静态服务建议传 0。</param>
        /// <param name="initialLOD">初始 LOD 等级，默认为 <see cref="UpdateLOD.Tier0"/>。
        /// 注意此处的档位是每 2^k 个<b>固定步</b>（默认 0.02s 一步），不是变步长轴的毫秒。</param>
        public static void RegisterFixed(IFixedUpdateable node, int depth, UpdateLOD initialLOD = UpdateLOD.Tier0)
        {
            if (node == null) return;
            SchedulerOf(UpdateTiming.FixedUpdate)?.Register(node, depth, initialLOD);
        }

        /// <summary>
        /// 注销一个可更新对象（任一时机）。
        /// </summary>
        /// <param name="node">要注销的对象。</param>
        public static void Unregister(IUpdateLifecycle node)
        {
            if (_schedulers == null || node == null) return;

            // 逐个时机转发：只有持有它的那套会真正删除，其余是空操作。
            // 门面不维护「节点属于哪个时机」的映射表——那是第二份真相，漏同步即幽灵条目
            for (int i = 0; i < _schedulers.Length; i++)
            {
                _schedulers[i].Unregister(node);
            }
        }

        #endregion

        #region Public API — 启用/禁用

        /// <summary>
        /// 启用指定对象的派发（任一时机）。
        /// <para>会触发 <see cref="IUpdateLifecycle.OnEnable"/>。</para>
        /// </summary>
        /// <param name="node">要启用的对象。</param>
        public static void Enable(IUpdateLifecycle node)
        {
            if (_schedulers == null || node == null) return;

            for (int i = 0; i < _schedulers.Length; i++)
            {
                _schedulers[i].Enable(node);
            }
        }

        /// <summary>
        /// 禁用指定对象的派发（任一时机）。
        /// <para>会触发 <see cref="IUpdateLifecycle.OnDisable"/>。</para>
        /// </summary>
        /// <param name="node">要禁用的对象。</param>
        public static void Disable(IUpdateLifecycle node)
        {
            if (_schedulers == null || node == null) return;

            for (int i = 0; i < _schedulers.Length; i++)
            {
                _schedulers[i].Disable(node);
            }
        }

        /// <summary>
        /// 检查对象是否处于启用状态（任一时机）。
        /// </summary>
        /// <param name="node">要检查的对象。</param>
        /// <returns>如果对象未被禁用则返回 true。</returns>
        public static bool IsEnabled(IUpdateLifecycle node)
        {
            if (_schedulers == null || node == null) return false;

            // 取与：只要有一套调度器认为它被禁用就是禁用。
            // 未注册过的对象在每套里都返回 true，与「未注册返回 true」的既有语义一致
            for (int i = 0; i < _schedulers.Length; i++)
            {
                if (!_schedulers[i].IsEnabled(node)) return false;
            }
            return true;
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
            ProcessImmediate(node, deltaTime, new UpdateClock(time, time));
        }

        /// <summary>
        /// 立即对指定对象执行一次更新并重新调整 LOD，时刻按对象所属的时间轴从时钟中取。
        /// </summary>
        /// <param name="node">要立即更新的对象。</param>
        /// <param name="deltaTime">传入的时间差。</param>
        /// <param name="clock">本帧的时间基。</param>
        public static void ProcessImmediate(IUpdateable node, float deltaTime, in UpdateClock clock)
        {
            if (_schedulers == null || node == null) return;

            for (int i = 0; i < _schedulers.Length; i++)
            {
                _schedulers[i].ProcessImmediate(node, deltaTime, clock);
            }
        }

        #endregion

        #region Public API — 查询

        /// <summary>
        /// 获取指定 <see cref="UpdateLOD"/> 等级的对象数量（含全部时机与时间轴）。
        /// </summary>
        public static int GetCount(UpdateLOD lod)
        {
            if (_schedulers == null) return 0;

            int count = 0;
            for (int i = 0; i < _schedulers.Length; i++)
            {
                count += _schedulers[i].GetCount(lod);
            }
            return count;
        }

        /// <summary>
        /// 获取所有 LOD 等级的对象总数（不含禁用对象，含全部时机与时间轴）。
        /// </summary>
        public static int TotalCount
        {
            get
            {
                if (_schedulers == null) return 0;

                int count = 0;
                for (int i = 0; i < _schedulers.Length; i++)
                {
                    count += _schedulers[i].TotalCount;
                }
                return count;
            }
        }

        /// <summary>
        /// 获取禁用对象数量（含全部时机）。
        /// </summary>
        public static int DisabledCount
        {
            get
            {
                if (_schedulers == null) return 0;

                int count = 0;
                for (int i = 0; i < _schedulers.Length; i++)
                {
                    count += _schedulers[i].DisabledCount;
                }
                return count;
            }
        }

        #endregion
    }
}