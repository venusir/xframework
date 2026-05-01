using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 节点树启动扩展方法。
    /// <para>提供 <see cref="IParentNode"/> 的启动管线：装载 → 加载 → 启动 → 回收。</para>
    /// </summary>
    public static class StartupExtensions
    {
        /// <summary>
        /// 启动节点树。依次执行装载、加载、启动、回收四个阶段：
        /// <para>1. 装载：扫描节点树，收集实现了 <see cref="ILoadable"/> 的节点。</para>
        /// <para>2. 加载：等待所有加载任务完成。</para>
        /// <para>3. 启动：递归启动所有节点的 <see cref="BaseNode.OnStart"/>。</para>
        /// <para>4. 回收：销毁加载器，清理资源。</para>
        /// </summary>
        /// <param name="root">节点树的根节点。</param>
        /// <param name="progress">可选的进度报告回调，用于接收启动各阶段的进度快照。</param>
        public static async UniTask StartupAsync(this IParentNode root, System.IProgress<LoadProgressSnapshot> progress = null)
        {
            if (root == null)
                return;

            // 阶段 1: 装载 — 扫描节点树，收集加载任务
            progress?.Report(new LoadProgressSnapshot
            {
                OverallProgress = 0f,
                Description = "Scanning nodes...",
                CurrentTaskName = null,
                TotalTaskCount = 0,
                CompletedCount = 0,
                FailedCount = 0,
            });

            ILoader loader = new Loader();
            root.CollectLoadables(loader);

            // 阶段 2: 加载 — 执行所有加载任务
            loader.OnProgressUpdate += snapshot => progress?.Report(snapshot);
            await loader.LoadAsync();

            // 阶段 3: 启动 — 递归启动所有节点
            progress?.Report(new LoadProgressSnapshot
            {
                OverallProgress = 1f,
                Description = "Starting nodes...",
                CurrentTaskName = null,
                TotalTaskCount = 0,
                CompletedCount = 0,
                FailedCount = 0,
            });

            if (root is BaseNode baseNode)
                baseNode.Start();

            // 阶段 4: 回收
            loader.Destroy();
        }

        /// <summary>
        /// 从指定根节点开始，递归查找所有实现了 <see cref="ILoadable"/> 的节点并注册到 <see cref="ILoader"/>。
        /// <para>注册后，<see cref="ILoader.LoadAsync"/> 时会统一调度这些节点的加载任务。</para>
        /// </summary>
        /// <param name="root">搜索的起始节点。</param>
        /// <param name="loader">加载器实例。</param>
        public static void CollectLoadables(this IParentNode root, ILoader loader)
        {
            if (root == null || loader == null)
                return;

            for (int i = 0; i < root.ChildCount; i++)
            {
                var child = root[i];

                if (child is ILoadable loadable)
                {
                    loader.AddLoadable(loadable);
                }

                if (child is IParentNode childParent)
                {
                    CollectLoadables(childParent, loader);
                }
            }
        }
    }
}
