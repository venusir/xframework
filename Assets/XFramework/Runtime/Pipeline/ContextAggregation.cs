using UnityEngine;

namespace XFramework.XPipeline
{

    /// <summary>
    /// 加权聚合扫描共享助手(内部)。管线实现(<see cref="PipelineImpl"/>)与容器聚合器
    /// (<see cref="StageAggregator"/>)的「加权扫描 + 阈值节流」原为同一算法的双份拷贝,
    /// 由本助手收敛为单一实现;两调用方的逐分支差异并入 <see cref="ScanMode"/> 开关,
    /// 各自的差异后缀(管线:字段落位 + 广播;聚合器:诊断优先覆盖 + 转发主上下文 + 失败中断兄弟 +
    /// 重入/迟写防护)保留在调用方。
    /// <para>事件驱动每写触发一次,本文件全部成员零 LINQ、零分配(只读结构回传)。</para>
    /// </summary>
    internal static class ContextAggregation
    {
        #region Public API

        /// <summary>
        /// 运行中未写描述时的广播/转发占位(空串:无描述即无文案,不与任何真实/终局文案撞车)。
        /// <para>完成终局文案 "Completed" 由 <see cref="PipelineImpl"/> 终局块显式写出,不回落本占位;
        /// 脏判定比较的是原始描述(可能 null),本占位只发生在广播/转发映射点,节流语义不受影响。</para>
        /// </summary>
        internal const string RunningDescriptionPlaceholder = "";

        /// <summary>扫描语义开关:两调用方逐分支差异的收敛点。</summary>
        internal enum ScanMode
        {
            /// <summary>顶层阶段(管线):失败分支不产出描述/任务名;任务名原始透传;仅执行中阶段记录当前阶段名。</summary>
            TopLevel,

            /// <summary>组内子阶段(容器聚合器):失败分支产出描述/任务名并捕获首失败诊断;任务名空则回退阶段名;不记录阶段名。</summary>
            Group,
        }

        /// <summary>
        /// 一次加权聚合的扫描结果(只读结构,零 GC)。
        /// <para>加权进度 + 当前描述/阶段名/任务名 + 完成/失败计数 + 首失败诊断(供聚合器诊断优先使用)。
        /// 描述与任务名在扫描序上取「最近一次写入」,失败诊断取「数组序第一个进入 Failed 的上下文」。</para>
        /// </summary>
        internal readonly struct ScanResult
        {
            /// <summary>加权进度 Σ(w·p)/Σ(w),权重和为 0 时为 0(全 Weight0 场景回落 0,语义正确)。</summary>
            public readonly float Overall;

            /// <summary>当前描述(扫描序最近一次写入;顶层模式仅执行中产出,组模式含失败)。</summary>
            public readonly string Description;

            /// <summary>当前执行阶段名(仅顶层模式,执行中阶段产出)。</summary>
            public readonly string StageName;

            /// <summary>当前任务名(顶层模式原始透传;组模式空则回退阶段名)。</summary>
            public readonly string TaskName;

            /// <summary>已完成上下文数。</summary>
            public readonly int CompletedCount;

            /// <summary>失败上下文数(组模式下即失败判定源)。</summary>
            public readonly int FailedCount;

            /// <summary>首失败子阶段的描述(诊断优先;顶层模式不消费)。</summary>
            public readonly string FailDescription;

            /// <summary>首失败子阶段的任务名。</summary>
            public readonly string FailTaskName;

            public ScanResult(float overall, string description, string stageName, string taskName,
                int completedCount, int failedCount, string failDescription, string failTaskName)
            {
                Overall = overall;
                Description = description;
                StageName = stageName;
                TaskName = taskName;
                CompletedCount = completedCount;
                FailedCount = failedCount;
                FailDescription = failDescription;
                FailTaskName = failTaskName;
            }
        }

        /// <summary>
        /// 单次加权聚合扫描:已完成记全权 w、执行中记 w·p、失败权重移出分子与分母(计数;
        /// 组模式再产出描述/任务名并捕获首失败诊断)、Pending 不占进度(默认分支忽略)。
        /// <para>与既有两处实现逐分支同构:遍历保持输入顺序、算术序不变(浮点结果 bit 级一致);
        /// 差异仅按 <paramref name="mode"/> 收敛——顶层失败分支不产出描述(管线失败描述由 RunAsync
        /// 直接读失败阶段上下文),组模式任务名空回退阶段名且仅组模式记录首失败诊断。</para>
        /// </summary>
        /// <param name="contexts">上下文数组(管线级 <see cref="PipelineImpl"/> 为顶层阶段数组,容器为子阶段数组)。</param>
        /// <param name="mode">扫描语义开关(<see cref="ScanMode.TopLevel"/> / <see cref="ScanMode.Group"/>)。</param>
        /// <returns>扫描结果(只读结构,零分配)。</returns>
        internal static ScanResult Scan(PipelineStageContext[] contexts, ScanMode mode)
        {
            float weightSum = 0f;
            float weightedSum = 0f;
            int completedCount = 0;
            int failedCount = 0;
            string currentDesc = null;
            string currentStageName = null;
            string currentTaskName = null;
            string failDescription = null;
            string failTaskName = null;

            for (int i = 0; i < contexts.Length; i++)
            {
                var ctx = contexts[i];

                switch (ctx.State)
                {
                    case PipelineStageState.Completed:
                        completedCount++;
                        weightSum += ctx.Weight;
                        weightedSum += ctx.Weight;
                        break;
                    case PipelineStageState.Failed:
                        failedCount++;
                        // 失败诊断只按组模式捕获:首失败捕获(数组序)在描述/状态写入序已定后不可变
                        if (mode == ScanMode.Group)
                        {
                            if (failDescription == null)
                            {
                                failDescription = ctx.Description;
                                failTaskName = ctx.CurrentTaskName ?? ctx.Name;
                            }
                            // 失败阶段也产出当前描述/任务名(诊断优先由调用方后缀用首失败覆盖完成)
                            currentDesc = ctx.Description;
                            currentTaskName = ctx.CurrentTaskName ?? ctx.Name;
                        }
                        break;
                    case PipelineStageState.Executing:
                        weightSum += ctx.Weight;
                        weightedSum += ctx.Weight * ctx.Progress;
                        currentDesc = ctx.Description;
                        currentTaskName = mode == ScanMode.Group
                            ? ctx.CurrentTaskName ?? ctx.Name
                            : ctx.CurrentTaskName;
                        if (mode == ScanMode.TopLevel)
                            currentStageName = ctx.Name;
                        break;
                    default:
                        // Pending:未开始子阶段不占进度(阶段切换回落属预期)
                        break;
                }
            }

            return new ScanResult(
                weightSum > 0f ? weightedSum / weightSum : 0f,
                currentDesc, currentStageName, currentTaskName,
                completedCount, failedCount, failDescription, failTaskName);
        }

        /// <summary>
        /// 阈值节流脏判定:总体进度变化 ≥1% || 描述变化 || 任一上下文状态变化(每帧路径零 LINQ)。
        /// <para>description 由调用方按各自口径传入(管线:原始扫描描述;聚合器:诊断优先覆盖后的描述),
        /// 与既有节流语义一致——占位归一化(如 <c>?? "Completed"</c>)发生在广播/转发映射点而非脏判定,
        /// 此处不做归一化(归一化不对称是既有行为,不得在此修正)。</para>
        /// </summary>
        /// <param name="overall">本次扫描加权进度。</param>
        /// <param name="description">本次扫描描述(调用方口径)。</param>
        /// <param name="lastOverall">上次广播加权进度。</param>
        /// <param name="lastDescription">上次广播描述(调用方口径)。</param>
        /// <param name="contexts">上下文数组。</param>
        /// <param name="lastStates">上次广播后快照的状态数组(与 <paramref name="contexts"/> 同序同长)。</param>
        /// <returns>是否脏(需广播/转发)。</returns>
        internal static bool IsDirty(float overall, string description,
            float lastOverall, string lastDescription,
            PipelineStageContext[] contexts, PipelineStageState[] lastStates)
        {
            bool dirty = Mathf.Abs(overall - lastOverall) >= 0.01f;
            if (!dirty && description != lastDescription)
                dirty = true;
            if (!dirty)
            {
                for (int i = 0; i < contexts.Length; i++)
                {
                    if (contexts[i].State != lastStates[i])
                    {
                        dirty = true;
                        break;
                    }
                }
            }
            return dirty;
        }

        /// <summary>广播/转发后把当前状态快照到节流比较数组(手写 for,零 LINQ)。</summary>
        internal static void CopyStates(PipelineStageContext[] contexts, PipelineStageState[] lastStates)
        {
            for (int i = 0; i < contexts.Length; i++)
                lastStates[i] = contexts[i].State;
        }

        #endregion
    }
}
