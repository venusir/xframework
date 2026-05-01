using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace XFramework
{
    /// <summary>
    /// YooAsset 初始化加载任务。
    /// <para>作为 <see cref="LoadableTask"/> 参与启动管线的加载阶段，完成 YooAsset 的初始化流程。</para>
    /// </summary>
    class YooAssetInitTask : LoadableTask
    {
        private readonly string _packageName;
        private ResourcePackage _package;

        /// <summary>初始化完成后的 ResourcePackage 实例。</summary>
        public ResourcePackage Package => _package;

        public YooAssetInitTask(string packageName = "DefaultPackage")
        {
            _packageName = packageName;
            Name = "YooAsset Init";
            Weight = 0.5f;
        }

        protected override async UniTask LoadAsync(CancellationToken cancellationToken)
        {
            SetDescription("Initializing YooAsset...");

            // 1. 初始化 YooAsset 全局环境
            if (!YooAssets.Initialized)
            {
                YooAssets.Initialize();
            }

            SetProgress(0.2f);

            // 2. 获取或创建资源包
            _package = YooAssets.TryGetPackage(_packageName);
            if (_package == null)
            {
                _package = YooAssets.CreatePackage(_packageName);
            }

            SetProgress(0.4f);
            SetDescription("Initializing resource package...");

            // 3. 初始化资源包（使用离线模式参数）
            var initParameters = new OfflinePlayModeParameters();
            var initOperation = _package.InitializeAsync(initParameters);
            await initOperation.WithCancellation(cancellationToken);

            if (initOperation.Status != EOperationStatus.Succeed)
            {
                SetDescription($"Package init failed: {initOperation.Error}");
                SetState(LoadState.Failed);
                return;
            }

            SetProgress(0.7f);
            SetDescription("Requesting package version...");

            // 4. 获取资源版本号
            var versionOperation = _package.RequestPackageVersionAsync();
            await versionOperation.WithCancellation(cancellationToken);

            if (versionOperation.Status != EOperationStatus.Succeed)
            {
                SetDescription($"Version request failed: {versionOperation.Error}");
                SetState(LoadState.Failed);
                return;
            }

            SetProgress(0.8f);
            SetDescription("Updating package manifest...");

            // 5. 更新资源清单
            var updateOperation = _package.UpdatePackageManifestAsync(versionOperation.PackageVersion);
            await updateOperation.WithCancellation(cancellationToken);

            if (updateOperation.Status != EOperationStatus.Succeed)
            {
                SetDescription($"Manifest update failed: {updateOperation.Error}");
                SetState(LoadState.Failed);
                return;
            }

            SetProgress(1f);
            SetDescription("YooAsset initialized.");
        }
    }
}
