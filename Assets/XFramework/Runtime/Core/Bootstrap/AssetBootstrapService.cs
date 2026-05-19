using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XAsset;
using XFramework.XLoader;

namespace XFramework.XCore
{
    /// <summary>
    /// <see cref="AssetManager"/> 的 <see cref="IModuleService"/> 适配器。
    /// <para>将资源管理器的初始化包装为 BootstrapNode 可统一管理的模块。</para>
    /// </summary>
    internal sealed class AssetBootstrapService : IModuleService
    {
        #region IModuleService

        public bool IsInitialized => AssetManager.IsInitialized;

        public async UniTask InitializeAsync(IProgress<LoadContext> progress, CancellationToken cancellationToken)
        {
            if (IsInitialized)
            {
                progress?.Report(new LoadContext { OverallProgress = 1f });
                return;
            }

            if (progress != null)
            {
                await AssetManager.InitializeAsync(progress, cancellationToken);
            }
            else
            {
                await AssetManager.InitializeAsync(cancellationToken);
            }
        }

        public void Dispose()
        {
            AssetManager.Destroy();
        }

        #endregion
    }
}