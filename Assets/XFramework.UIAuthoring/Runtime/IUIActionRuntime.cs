using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 动作执行器依赖的 UI 运行时接缝。
    /// <para>生产实现是 <see cref="UIManagerActionRuntime"/>；测试注入 fake，避免依赖核心 internal 的工厂。</para>
    /// </summary>
    internal interface IUIActionRuntime
    {
        bool IsDeclarativePanelOpen { get; }

        UIDeclarativePanel GetDeclarativePanel();

        UniTask<UIDeclarativePanel> OpenDeclarativeAsync(string assetPath, int layer, CancellationToken cancellationToken);

        UniTask<UIPanelBase> OpenCSharpAsync(string assetPath, int layer, CancellationToken cancellationToken);

        UniTask<UIPanelBase> PushCSharpAsync(string assetPath, int layer, CancellationToken cancellationToken);

        /// <summary>
        /// 关闭当前声明式面板并打开目标声明式面板。
        /// <para>关闭会使当前 session 失效；因此「关闭后打开」必须在同一运行时方法内完成，
        /// 执行器不能在 <c>CloseAsync</c> 返回后自行继续。</para>
        /// </summary>
        UniTask ReplaceDeclarativeAsync(UIPanelBase current, string targetAssetPath, int layer,
            CancellationToken cancellationToken);

        UniTask PopAsync(bool immediate, CancellationToken cancellationToken);

        UniTask CloseAsync(UIPanelBase panel, bool immediate, CancellationToken cancellationToken);

        UniTask CloseAllAsync(bool immediate, CancellationToken cancellationToken);

        UniTask ShowTipAsync(string text, TipConfig config, CancellationToken cancellationToken);

        void SetLayerVisibility(int layer, bool visible);

        void SetLayerInteractive(int layer, bool interactive);
    }
}
