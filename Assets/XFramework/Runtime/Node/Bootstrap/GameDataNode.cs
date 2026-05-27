using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XData;
using XFramework.XLoader;

namespace XFramework.XNode
{
    /// <summary>
    /// Data 模块的节点树桥梁。挂载在 <see cref="ServiceInitializerNode"/> 下，
    /// 负责在加载阶段初始化 <see cref="XData.DataManager"/> 静态门面。
    /// </summary>
    internal sealed class GameDataNode : LeafNode, ILoadable
    {
        #region ILoadable

        /// <summary>
        /// Phase = 3。晚于 Config(1)、Asset(0)，确保依赖的模块已就绪。
        /// </summary>
        public int Phase => 3;

        public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
        {
            progress.SetDescription("Initializing Data Manager...");

            var impl = new DataManagerImpl();
            DataManager.Initialize(impl);

            progress.SetProgress(1f);
            progress.SetState(LoadState.Completed);
        }

        #endregion

        #region Lifecycle

        protected override void OnDestroy()
        {
            DataManager.Shutdown();
            base.OnDestroy();
        }

        #endregion
    }
}