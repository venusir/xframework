using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XLoader;
using XFramework.XSave;

namespace XFramework.XNode
{
    /// <summary>
    /// Save 模块的节点树桥梁。挂载在 <see cref="ServiceInitializerNode"/> 下，
    /// 负责在加载阶段初始化 <see cref="XSave.SaveManager"/> 静态门面。
    /// </summary>
    internal sealed class SaveBootstrapNode : LeafNode, ILoadable
    {
        #region ILoadable

        /// <summary>
        /// Phase = 4。晚于 Data(3)，确保快照能力已就绪。
        /// </summary>
        public int Phase => 4;

        public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
        {
            progress.SetDescription("Initializing Save Manager...");

            SaveManager.Initialize();

            progress.SetProgress(1f);
            progress.SetState(LoadState.Completed);
        }

        #endregion

        #region Lifecycle

        protected override void OnDestroy()
        {
            SaveManager.Shutdown();
            base.OnDestroy();
        }

        #endregion
    }
}
