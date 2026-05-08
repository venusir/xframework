using System;

namespace XFramework
{
    /// <summary>
    /// 将 <see cref="LoadContext"/> 适配为 <see cref="IProgress{LoadContext}"/>。
    /// <para>用于 <see cref="AssetNode"/> 的 <see cref="ILoadable.LoadAsync"/> 将节点树启动管线的进度报告桥接到 <see cref="AssetSystem.InitializeAsync(IProgress{LoadContext}, System.Threading.CancellationToken)"/>。</para>
    /// </summary>
    internal class LoadContextProgress : IProgress<LoadContext>
    {
        private readonly LoadContext _target;

        public LoadContextProgress(LoadContext target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public void Report(LoadContext value)
        {
            _target.SetProgress(value.OverallProgress);
            _target.SetDescription(value.Description);

            if (value.OverallProgress >= 1f)
            {
                _target.SetState(LoadState.Completed);
            }
        }
    }
}
