using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XPipeline
{
    /// <summary>
    /// 管线门面。提供 <see cref="IPipeline"/> 实例的创建入口(实例即用即弃,非全局单例),
    /// 以及按相位分组装配的编排助手。
    /// </summary>
    public static class Pipeline
    {
        /// <summary>创建管线实例。</summary>
        public static IPipeline Create() => new PipelineImpl();

        /// <summary>
        /// 按相位分组装配阶段清单:输入按 <see cref="IPhaseStage.Phase"/> 升序分组,
        /// 每组装配为一个 <see cref="ParallelStage"/>(组内并行、组内保持输入顺序,组间由管线串行)。
        /// <para>装配结果可直接逐个 <see cref="IPipeline.AddStage(IPipelineStage)"/> 添加执行;
        /// 同相位并行、相位升序串行的声明式编排即「每相位一个并行阶段」的约定装配
        /// (如 Bootstrap 的启动管线按相位分组调度)。</para>
        /// </summary>
        /// <param name="stages">相位阶段列表。null 抛 <see cref="ArgumentNullException"/>;元素不得为 null;
        /// 空列表返回空清单。</param>
        /// <param name="nameFormat">相位组名格式(相位号作为格式化参数),默认 "Phase-{0}"。
        /// null 抛 <see cref="ArgumentNullException"/>。</param>
        /// <returns>按相位升序排列的 <see cref="ParallelStage"/> 清单,每组一个。</returns>
        public static IReadOnlyList<IPipelineStage> BuildPhaseGroups(
            IReadOnlyList<IPhaseStage> stages, string nameFormat = "Phase-{0}")
        {
            if (stages == null)
                throw new ArgumentNullException(nameof(stages));
            if (nameFormat == null)
                throw new ArgumentNullException(nameof(nameFormat));

            // 按相位分组(组内保持输入顺序,装配期一次遍历,零 LINQ)
            var groups = new Dictionary<int, List<IPhaseStage>>();
            for (int i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                if (stage == null)
                    throw new ArgumentException("stages must not contain null.", nameof(stages));

                if (!groups.TryGetValue(stage.Phase, out var list))
                {
                    list = new List<IPhaseStage>();
                    groups.Add(stage.Phase, list);
                }
                list.Add(stage);
            }

            // 相位升序,每相位一个并行阶段
            var phases = new List<int>(groups.Keys);
            phases.Sort();

            var result = new List<IPipelineStage>(groups.Count);
            for (int i = 0; i < phases.Count; i++)
            {
                int phase = phases[i];
                result.Add(new ParallelStage(groups[phase], string.Format(nameFormat, phase)));
            }
            return result;
        }
    }

    /// <summary>
    /// 管线实现:阶段串行编排 + 进度加权聚合 + 失败/取消传播。
    /// <para>阶段经 <see cref="PipelineStageContext"/> 主动写入(事件驱动),管线不轮询、不持有帧泵;
    /// 阶段串行逐 await,天然保证 <see cref="RunAsync"/> 返回时无在途阶段任务。</para>
    /// </summary>
    internal sealed class PipelineImpl : IPipeline, IStageContextSink, IContextBellSink
    {
        #region IPipeline Properties

        public bool IsRunning => _status == PipelineStatus.Running;

        public PipelineStatus Status => _status;

        public string FailureReason { get; private set; }

        #endregion

        #region IPipeline Events

        public event Action<PipelineProgress> OnProgressUpdate;
        public event Action OnCompleted;
        public event Action OnCancelled;
        public event Action<string> OnFailed;

        #endregion

        #region Private Fields

        /// <summary>阶段清单。非 readonly:<see cref="Destroy"/> 刻意<b>替换</b>而非清空(在途循环持有旧引用)。</summary>
        List<IPipelineStage> _stages = new List<IPipelineStage>();

        /// <summary>与 <see cref="_stages"/> 同序的超时配置(秒,0/负值/NaN = 不启用)。</summary>
        List<float> _stageTimeouts = new List<float>();

        /// <summary>
        /// 超时计时任务工厂(测试缝,internal):生产路径为真实墙钟延时(<see cref="DelayType.Realtime"/>,不受 timeScale 影响);
        /// EditMode 测试注入确定性计时器(阻塞式测试中真实延时由 PlayerLoop 泵送,永不触发)。
        /// </summary>
        internal Func<float, CancellationToken, UniTask> TimeoutTaskFactory =
            (seconds, token) => UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime, cancellationToken: token);

        /// <summary>当前运行装配的阶段上下文(事件驱动聚合的读取源)。</summary>
        PipelineStageContext[] _contexts;

        /// <summary>写入门铃:重入折叠 + 迟写防护(与容器聚合器共用同一实现)。</summary>
        readonly ContextBell _bell = new ContextBell();

        /// <summary>终局状态单一真值(<see cref="IsRunning"/> 由它派生);运行结束后保持终局值,不回落。</summary>
        PipelineStatus _status;

        /// <summary>
        /// 聚合扫描次数(测试缝,internal、按实例累计)。阶段每写一次即一次扫描,顶层节流只决定
        /// 要不要广播、不减少扫描本身——于是「组内节流失效带来多少次顶层重扫」在公开面(广播序列)
        /// 上完全不可观测:广播被顶层节流吞掉了,只能由本计数读出。
        /// </summary>
        internal int AggregationCount { get; private set; }

        /// <summary>上一帧广播快照:全局进度 + 描述 + 任务名 + 各阶段状态(阈值节流,首帧 -1 保证必广播)。</summary>
        float _lastOverall = -1f;
        string _lastDesc;
        string _lastTaskName;
        PipelineStageState[] _lastStates;

        /// <summary>最近一次广播快照。</summary>
        float _overall;
        string _description;
        string _currentStageName;
        string _currentTaskName;
        int _completedStageCount;
        int _failedStageCount;

        #endregion

        #region IPipeline Methods

        public void AddStage(IPipelineStage stage)
        {
            AddStage(stage, 0f);
        }

        public void AddStage(IPipelineStage stage, float timeoutSeconds)
        {
            if (stage == null) return;

            // 运行期装配防护:管线运行中追加阶段打警告并忽略(与 RunAsync 重入守卫同风格);
            // 运行中 _contexts 已按启动时刻快照,入列会使阶段列表与上下文数组错位(越界/语义未定义)
            if (IsRunning)
            {
                LogManager.Warning(LogCategories.Pipeline, "AddStage: already running, ignore this call.");
                return;
            }

            // 避免重复添加(超时在首次添加时一并固化,平行列表同序)
            for (int i = 0; i < _stages.Count; i++)
            {
                if (_stages[i] == stage)
                    return;
            }

            _stages.Add(stage);
            _stageTimeouts.Add(timeoutSeconds);
        }

        public async UniTask RunAsync(CancellationToken cancellationToken = default)
        {
            if (IsRunning)
            {
                LogManager.Warning(LogCategories.Pipeline, "RunAsync: already running, ignore this call.");
                return;
            }

            if (_stages.Count == 0)
            {
                LogManager.Warning(LogCategories.Pipeline, "RunAsync: no stages found.");
                _status = PipelineStatus.Completed; // 与紧随其后的 OnCompleted 保持一致
                DispatchSafely(OnCompleted, nameof(OnCompleted));
                return;
            }

            _status = PipelineStatus.Running;
            FailureReason = null; // 重跑时不得残留上一次的失败原因
            _bell.Reset();

            // 运行期局部别名:Destroy 会替换阶段清单、清空上下文数组,而在途循环在每个 await 之后
            // 仍会重读这些字段——不脱钩的话「运行中销毁」会被外层 catch 变成一条假的 RunAsync failed
            var stages = _stages;
            var timeouts = _stageTimeouts;
            int stageCount = stages.Count;

            // 每轮从干净快照开始(不只在 finally 复位:Destroy 中途调用时,上一轮的 finally 会晚于本轮赋值)
            _lastOverall = -1f;
            _lastDesc = null;
            _lastTaskName = null;

            // 链接外部取消令牌:任一方取消,当前阶段收到已取消的 token
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 装配阶段上下文(运行期一次分配)
            var contexts = new PipelineStageContext[stageCount];
            _contexts = contexts;
            _lastStates = new PipelineStageState[stageCount];
            for (int i = 0; i < stageCount; i++)
            {
                contexts[i] = new PipelineStageContext
                {
                    Owner = this,
                    Name = stages[i].Name,
                    Weight = stages[i].Weight,
                };
            }

            // 终局标志:失败/取消/完成三路互斥,取消与失败绝不落入完成块
            bool failed = false;
            bool cancelled = false;
            string failDescription = null;
            string failStageName = null;
            Exception failException = null;

            try
            {
                for (int i = 0; i < stageCount; i++)
                {
                    // 阶段边界取消检查:取消显式走取消终局,不落入完成块
                    if (cts.Token.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    var stage = stages[i];
                    var ctx = contexts[i];

                    // 顶层阶段计时:System.Diagnostics 全限定(避免与 UnityEngine.Debug 冲突)
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    LogManager.Info(LogCategories.Pipeline, "Stage '{0}' start", stage.Name);
                    ctx.SetState(PipelineStageState.Executing);

                    // 阶段经共享包装统一执行(异常/取消捕获 + 契约兜底),返回是否以取消结束
                    var stageTask = StageExecution.RunStageAsync(stage, ctx, cts.Token);

                    bool stageCancelled;
                    float timeoutSeconds = timeouts[i];
                    if (timeoutSeconds > 0f)
                    {
                        // 超时竞速:独立超时 CTS 与链内取消联动(外部取消经链接同步传播给计时任务);
                        // WhenAny 内部观测全部任务,被放弃的在途任务无未观察异常
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        var timeoutTask = TimeoutTaskFactory(timeoutSeconds, timeoutCts.Token);

                        var (raceCancelled, race) = await UniTask.WhenAny<bool>(stageTask, timeoutTask).SuppressCancellationThrow();
                        if (raceCancelled)
                        {
                            // 竞速期间外部取消:统一走取消终局
                            LogManager.Info(LogCategories.Pipeline, "Stage '{0}' cancelled in {1:F0}ms",
                                stage.Name, sw.Elapsed.TotalMilliseconds);
                            cancelled = true;
                            break;
                        }

                        if (!race.hasResultLeft)
                        {
                            // 超时获胜:中断在途阶段但不等待其沉降(挂死任务不得阻塞管线,断行后终局块与 finally
                            // 之间无 await,被放弃任务无覆写终局广播的窗口)
                            cts.Cancel();
                            ctx.SetDescription($"Stage '{stage.Name}' timed out after {timeoutSeconds}s");
                            ctx.SetState(PipelineStageState.Failed);
                            LogManager.Info(LogCategories.Pipeline, "Stage '{0}' timed out in {1:F0}ms",
                                stage.Name, sw.Elapsed.TotalMilliseconds);
                            failed = true;
                            failDescription = ctx.Description;
                            break;
                        }

                        // 阶段先完成:终止悬挂的计时任务
                        timeoutCts.Cancel();
                        stageCancelled = race.result;
                    }
                    else
                    {
                        stageCancelled = await stageTask;
                    }

                    if (stageCancelled)
                    {
                        LogManager.Info(LogCategories.Pipeline, "Stage '{0}' cancelled in {1:F0}ms",
                            stage.Name, sw.Elapsed.TotalMilliseconds);
                        cancelled = true;
                        break;
                    }

                    if (ctx.State == PipelineStageState.Failed)
                    {
                        LogManager.Info(LogCategories.Pipeline, "Stage '{0}' failed in {1:F0}ms",
                            stage.Name, sw.Elapsed.TotalMilliseconds);
                        failed = true;
                        failDescription = ctx.Description;
                        // 失败原因要能指名阶段(与容器日志同一「最具体的名字」口径):
                        // 容器主上下文的 CurrentTaskName 由聚合器写成首失败子阶段的名字;
                        // 直接挂到管线的阶段没上报过任务名,回落它自己的 Name
                        failStageName = ctx.CurrentTaskName ?? ctx.Name;
                        failException = ctx.FailureException;
                        break;
                    }

                    LogManager.Info(LogCategories.Pipeline, "Stage '{0}' completed in {1:F0}ms",
                        stage.Name, sw.Elapsed.TotalMilliseconds);
                }

                // 循环收尾:阶段沉降后 token 已取消且未失败 → 取消
                // (阶段可以对取消无感并正常返回,那样最后一个阶段会直接落进完成终局,取消就此丢失;
                // 与容器侧的沉降后检查对齐——三层终局判据一致)
                if (!failed && !cancelled && cts.Token.IsCancellationRequested)
                    cancelled = true;

                // 终局:取消 / 失败 / 完成三路互斥(先重算快照广播,再触发事件)
                // 关闸:终局广播与终局事件期间的重入写不再聚合——否则订阅者在事件里回写会再广播一次,
                // 于是「OnCancelled/OnCompleted 之后仍收到进度广播」
                _bell.Settle();
                if (cancelled)
                {
                    RecalculateSnapshot();
                    Broadcast();
                    _status = PipelineStatus.Cancelled;
                    DispatchSafely(OnCancelled, nameof(OnCancelled));
                    LogManager.Warning(LogCategories.Pipeline, "Pipeline cancelled.");
                }
                else if (failed)
                {
                    RecalculateSnapshot();
                    Broadcast();
                    _status = PipelineStatus.Failed;
                    FailureReason = $"Failed: {failStageName}: {failDescription}";
                    DispatchSafely(OnFailed, FailureReason, nameof(OnFailed));
                    // 有异常对象（阶段抛出，含容器内子阶段）就把堆栈带进 JSONL 的 exc 字段；
                    // 超时与「阶段主动置失败」没有异常，走原来的纯文本形态
                    if (failException != null)
                    {
                        LogManager.Exception(LogCategories.Pipeline, failException,
                            string.Format("Pipeline failed: {0}", failDescription));
                    }
                    else
                    {
                        LogManager.Error(LogCategories.Pipeline, "Pipeline failed: {0}", failDescription);
                    }
                }
                else
                {
                    // 完成:全局进度补满并广播(完成语义与各阶段权重无关)
                    _overall = 1f;
                    _description = "Completed";
                    // 让节流快照与刚广播的内容一致:终局后闸已关、不再有聚合,此行不留暗坑
                    _lastDesc = _description;
                    _currentStageName = null;
                    _currentTaskName = null;
                    _completedStageCount = _stages.Count;
                    _failedStageCount = 0;
                    _status = PipelineStatus.Completed;
                    Broadcast();
                    DispatchSafely(OnCompleted, nameof(OnCompleted));
                }
            }
            catch (OperationCanceledException)
            {
                // 防御分支:理论上不可达(RunStage 已吞掉全部 OCE);语义统一为取消,绝不静默
                _status = PipelineStatus.Cancelled;
                DispatchSafely(OnCancelled, nameof(OnCancelled));
            }
            catch (Exception ex)
            {
                LogManager.Exception(LogCategories.Pipeline, ex, "RunAsync failed");
                _status = PipelineStatus.Failed;
                FailureReason = $"Exception: {ex.Message}";
                DispatchSafely(OnFailed, FailureReason, nameof(OnFailed));
            }
            finally
            {
                // 终局状态保持:不在此处回落 Idle,否则 RunAsync 返回后就读不到本次结果了
                _contexts = null;
                _lastStates = null;
                _lastOverall = -1f;
                _lastDesc = null;
                _lastTaskName = null;
            }
        }

        public void Destroy()
        {
            // 替换而非 Clear:在途运行持有旧列表引用,Clear 会让它的索引越界(见 RunAsync 的局部别名)
            _stages = new List<IPipelineStage>();
            _stageTimeouts = new List<float>();

            OnProgressUpdate = null;
            OnCompleted = null;
            OnCancelled = null;
            OnFailed = null;

            _status = PipelineStatus.Idle;
            FailureReason = null;
            _contexts = null;
            _lastStates = null;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 阶段写入触发的门铃入口(<see cref="IStageContextSink"/> 实现,事件驱动,零闭包):
        /// 重入折叠与迟写防护交给共享门铃,折叠后的聚合见 <see cref="IContextBellSink.AggregateOnce"/>。
        /// </summary>
        public void OnStageContextChanged(PipelineStageContext changed)
        {
            // 运行结束/销毁后的写入即死信(终局窗口那一半由门铃的关闸负责)
            if (!IsRunning || _contexts == null) return;

            _bell.Ring(this);
        }

        /// <summary>
        /// 一次顶层加权聚合(<see cref="IContextBellSink"/> 实现):经共享扫描
        /// (<see cref="ContextAggregation.Scan"/>,顶层模式)加权聚合 Σ(w·p)/Σ(w),阈值节流后广播。
        /// <para>已完成阶段记 w,执行中记 w·p;失败阶段权重移出分子与分母;Weight=0 阶段不占进度。</para>
        /// </summary>
        void IContextBellSink.AggregateOnce()
        {
            AggregationCount++;
            var snap = ContextAggregation.Scan(_contexts, ContextAggregation.ScanMode.TopLevel);

            // 阈值节流:总体进度变化 ≥1% || 描述变化 || 任务名变化 || 任一阶段状态变化(共享扫描,每帧路径零 LINQ)
            if (ContextAggregation.IsDirty(snap.Overall, snap.Description, snap.TaskName,
                _lastOverall, _lastDesc, _lastTaskName, _contexts, _lastStates))
            {
                _overall = snap.Overall;
                _description = snap.Description;
                _currentStageName = snap.StageName;
                _currentTaskName = snap.TaskName;
                _completedStageCount = snap.CompletedCount;
                _failedStageCount = snap.FailedCount;

                _lastOverall = snap.Overall;
                _lastDesc = snap.Description;
                _lastTaskName = snap.TaskName;
                ContextAggregation.CopyStates(_contexts, _lastStates);

                Broadcast();
            }
        }

        /// <summary>无条件重算最新聚合快照(终局广播前调用,不受节流限制)。</summary>
        private void RecalculateSnapshot()
        {
            if (_contexts == null) return;

            var snap = ContextAggregation.Scan(_contexts, ContextAggregation.ScanMode.TopLevel);

            _overall = snap.Overall;
            _description = snap.Description;
            _currentStageName = snap.StageName;
            _currentTaskName = snap.TaskName;
            _completedStageCount = snap.CompletedCount;
            _failedStageCount = snap.FailedCount;
        }

        /// <summary>
        /// 构造进度快照并广播。描述为空(null)时回落空串占位(<see cref="ContextAggregation.RunningDescriptionPlaceholder"/>,
        /// 运行中未写描述的阶段不伪造终局文案);完成终局 "Completed" 由终局块显式写入字段,不经本映射。
        /// </summary>
        private void Broadcast()
        {
            var progress = new PipelineProgress
            {
                OverallProgress = _overall,
                Description = _description ?? ContextAggregation.RunningDescriptionPlaceholder,
                CurrentStageName = _currentStageName,
                CurrentTaskName = _currentTaskName,
                TotalStageCount = _stages.Count,
                CompletedStageCount = _completedStageCount,
                FailedStageCount = _failedStageCount,
            };
            DispatchSafely(OnProgressUpdate, progress, nameof(OnProgressUpdate));
        }

        /// <summary>
        /// 事件投递统一入口:订阅者异常隔离(记 Error 日志后继续),不让订阅者的 bug 冒泡进阶段栈。
        /// <para>为什么必须隔离:事件在阶段写入栈内同步触发——进度订阅者抛出的异常会顺着
        /// <c>Broadcast → PipelineStageContext.NotifyChanged → 阶段 ExecuteAsync</c> 冒到
        /// <see cref="StageExecution"/> 的捕获里,把无辜阶段置 Failed;终局事件抛异常更会把成功报成失败
        /// (冒到外层 catch 触发 <see cref="OnFailed"/>)。容器聚合器早有同款防护(见 <see cref="StageAggregator"/>),
        /// 这里是管线侧本该同构的另一半。</para>
        /// <para>粒度取舍:整条多播一次 try/catch,不用 <c>GetInvocationList</c> 逐个隔离——后者每次派发
        /// 分配一个委托数组,而进度广播在节流路径上;代价是抛异常的订阅者会饿死同一次派发中排在它后面的
        /// 订阅者(下一次派发照常,且日志已记下)。终局三事件每轮只触发一次,不额外优化。</para>
        /// </summary>
        private static void DispatchSafely<T>(Action<T> handlers, T arg, string eventName)
        {
            if (handlers == null) return;

            try
            {
                handlers.Invoke(arg);
            }
            catch (Exception ex)
            {
                LogManager.Exception(LogCategories.Pipeline, ex,
                    string.Format("{0} subscriber threw", eventName));
            }
        }

        /// <summary>无参事件重载,语义同有参版本(仅载荷不同)。</summary>
        private static void DispatchSafely(Action handlers, string eventName)
        {
            if (handlers == null) return;

            try
            {
                handlers.Invoke();
            }
            catch (Exception ex)
            {
                LogManager.Exception(LogCategories.Pipeline, ex,
                    string.Format("{0} subscriber threw", eventName));
            }
        }

        #endregion
    }
}
