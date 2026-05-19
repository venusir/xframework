using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XAsset;
using XFramework.XLoader;

namespace XFramework.XCore
{
    /// <summary>
    /// <see cref="AssetManager"/> 的启动节点。将资源管理器的初始化封装为节点树中的一个加载任务。
    /// <para>继承 <see cref="EntityNode"/>，实现 <see cref="ILoadable"/>，会被 <see cref="StartupExtensions"/>
    /// 自动收集并在加载阶段按 Phase 顺序执行。</para>
    /// </summary>
    internal sealed class AssetBootstrapNode : EntityNode, ILoadable
    {
        #region ILoadable

        /// <summary>
        /// Phase = 0。确保 Asset 模块最先被加载。
        /// </summary>
        public int Phase => 0;

        public async UniTask LoadAsync(LoadContext context, CancellationToken cancellationToken)
        {
            if (AssetManager.IsInitialized)
            {
                context.SetProgress(1f);
                context.SetState(LoadState.Completed);
                return;
            }

            context.SetDescription("Initializing Asset Manager...");
            var progress = new LoadContextProgress(context);
            await AssetManager.InitializeAsync(progress, cancellationToken);

            context.SetProgress(1f);
            context.SetState(LoadState.Completed);
        }

        #endregion

        #region Lifecycle

        protected override void OnDestroy()
        {
            AssetManager.Destroy();
            base.OnDestroy();
        }

        #endregion

        #region Private Types

        /// <summary>
        /// 将 <see cref="LoadContext"/> 适配为 <see cref="IProgress{T}"/>，
        /// 使得 AssetManager 的进度报告能映射到加载管线的上下文中。
        /// </summary>
        private sealed class LoadContextProgress : IProgress<LoadContext>
        {
            private readonly LoadContext _context;

            public LoadContextProgress(LoadContext context)
            {
                _context = context;
            }

            public void Report(LoadContext value)
            {
                if (value == null) return;

                _context.SetProgress(value.OverallProgress);

                if (!string.IsNullOrEmpty(value.Description))
                {
                    _context.SetDescription(value.Description);
                }
            }
        }

        #endregion
    }
}