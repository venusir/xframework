namespace XFramework
{
    /// <summary>
    /// 加载进度快照。供 UI 读取当前加载状态的全部信息。
    /// <para>由 <see cref="ILoader"/> 在每帧轮询时生成，通过 <see cref="ILoader.OnProgressUpdate"/> 事件广播。</para>
    /// </summary>
    public struct LoadProgressSnapshot
    {
        /// <summary>总体加载进度，取值范围 0.0 ~ 1.0。</summary>
        public float OverallProgress;

        /// <summary>当前加载阶段的描述文字。</summary>
        public string Description;

        /// <summary>当前正在执行的任务名称。</summary>
        public string CurrentTaskName;

        /// <summary>总任务数。</summary>
        public int TotalTaskCount;

        /// <summary>已完成的任务数。</summary>
        public int CompletedCount;

        /// <summary>失败的任务数。</summary>
        public int FailedCount;
    }
}
