using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XLog;
using XFramework.XUI;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 声明式 UI 的推荐入口：单根守卫 + 对话框单飞。
    /// <para>直接调用 <c>UIManager.OpenAsync&lt;UIDeclarativePanel&gt;</c> 无法阻止第二个地址；
    /// 核心会按具体类型去重并静默返回第一个面板。</para>
    /// </summary>
    public static class UIAuthoringNavigation
    {
        /// <summary>
        /// 打开声明式面板。已有声明式面板时：同地址重聚焦，异地址拒绝并 Warning。
        /// </summary>
        public static UniTask<UIDeclarativePanel> OpenAsync(
            string assetPath,
            int layer = UILayers.Default,
            CancellationToken cancellationToken = default)
        {
            return OpenAsync(new UIManagerActionRuntime(), assetPath, layer, cancellationToken);
        }

        internal static async UniTask<UIDeclarativePanel> OpenAsync(
            IUIActionRuntime runtime,
            string assetPath,
            int layer,
            CancellationToken cancellationToken)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));

            if (string.IsNullOrEmpty(assetPath))
            {
                LogManager.Warning(UIAuthoringLog.Category, "OpenAsync: assetPath is empty.");
                return null;
            }

            UIDeclarativePanel existing = runtime.GetDeclarativePanel();
            if (existing != null)
            {
                if (string.Equals(existing.AssetPath, assetPath, StringComparison.Ordinal))
                    return await runtime.OpenDeclarativeAsync(assetPath, layer, cancellationToken);

                LogManager.Warning(UIAuthoringLog.Category,
                    "OpenAsync rejected: a different declarative panel is already open ('{0}').",
                    existing.AssetPath);
                return existing;
            }

            return await runtime.OpenDeclarativeAsync(assetPath, layer, cancellationToken);
        }
    }
}
