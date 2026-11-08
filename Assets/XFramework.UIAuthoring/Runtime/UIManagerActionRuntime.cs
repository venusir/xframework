using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XLog;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// <see cref="IUIActionRuntime"/> 的生产实现：全部转发到核心 <c>UIManager</c> 静态门面。
    /// </summary>
    internal sealed class UIManagerActionRuntime : IUIActionRuntime
    {
        public bool IsDeclarativePanelOpen => UIManager.IsOpen<UIDeclarativePanel>();

        public UIDeclarativePanel GetDeclarativePanel() => UIManager.GetPanel<UIDeclarativePanel>();

        public UniTask<UIDeclarativePanel> OpenDeclarativeAsync(string assetPath, int layer,
            CancellationToken cancellationToken)
        {
            return UIManager.OpenAsync<UIDeclarativePanel>(assetPath, layer, null, cancellationToken);
        }

        public UniTask<UIPanelBase> OpenCSharpAsync(string assetPath, int layer,
            CancellationToken cancellationToken)
        {
            return UIManager.OpenAsync<UIPanelBase>(assetPath, layer, null, cancellationToken);
        }

        public UniTask<UIPanelBase> PushCSharpAsync(string assetPath, int layer,
            CancellationToken cancellationToken)
        {
            return UIManager.PushAsync<UIPanelBase>(assetPath, layer, null, cancellationToken);
        }

        public async UniTask ReplaceDeclarativeAsync(UIPanelBase current, string targetAssetPath, int layer,
            CancellationToken cancellationToken)
        {
            await UIManager.CloseAsync(current, false, cancellationToken);

            // 控制器可能拦下关闭；此时当前面板仍在，不能再打开第二个声明式面板（单根）。
            if (UIManager.IsOpen<UIDeclarativePanel>())
            {
                LogManager.Warning(UIAuthoringLog.Category,
                    "Replace aborted: current declarative panel is still open (close was blocked).");
                return;
            }

            await UIManager.OpenAsync<UIDeclarativePanel>(targetAssetPath, layer, null, cancellationToken);
        }

        public UniTask PopAsync(bool immediate, CancellationToken cancellationToken)
        {
            return UIManager.PopAsync(immediate, cancellationToken);
        }

        public UniTask CloseAsync(UIPanelBase panel, bool immediate, CancellationToken cancellationToken)
        {
            return UIManager.CloseAsync(panel, immediate, cancellationToken);
        }

        public UniTask CloseAllAsync(bool immediate, CancellationToken cancellationToken)
        {
            return UIManager.CloseAllAsync(immediate, cancellationToken);
        }

        public UniTask ShowTipAsync(string text, TipConfig config, CancellationToken cancellationToken)
        {
            return UIManager.ShowTipAsync(text, config, cancellationToken);
        }

        public void SetLayerVisibility(int layer, bool visible)
        {
            UIManager.SetLayerVisibility(layer, visible);
        }

        public void SetLayerInteractive(int layer, bool interactive)
        {
            UIManager.SetLayerInteractive(layer, interactive);
        }
    }
}
