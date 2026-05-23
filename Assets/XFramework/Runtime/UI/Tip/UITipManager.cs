using UnityEngine;
using XFramework.XUI.View;
using XFramework.XAsset;

namespace XFramework.XUI
{
    /// <summary>
    /// Tip 管理器（内部实现）。
    /// <para>负责 Tip 预制体的实例化、层级容器的管理、生命周期调度。</para>
    /// <para>Tip 实例通过 <see cref="XAsset.AssetManager"/> 获取和回池，不自行维护对象池。</para>
    /// <para>所有 Tip 挂载在 UIRoot 下独立的 Layer_Tip 容器中，使用极高的 sorting order 确保在最顶层显示。</para>
    /// </summary>
    internal static class UITipManager
    {
        #region Constants

        /// <summary>
        /// Tip 预制体的 YooAsset 地址。
        /// <para>第三方项目需要在 Resources 或 YooAsset 包中提供此预制体。</para>
        /// </summary>
        private const string TipAssetPath = "PF_UITipText";

        /// <summary>
        /// Tip 层级。数值极高，确保在所有面板之上。
        /// </summary>
        private const int TipLayer = 999;

        /// <summary>
        /// 层级容器名称。
        /// </summary>
        private const string TipLayerContainerName = "Layer_Tip";

        #endregion

        #region Cached References

        private static Transform _tipContainer;
        private static Canvas _tipContainerCanvas;

        #endregion

        #region Public API

        /// <summary>
        /// 显示一个 Tip。
        /// <para>异步加载预制体，实例化后播放动画，结束后自动回池。</para>
        /// </summary>
        /// <param name="text">显示文字。</param>
        /// <param name="config">显示配置。传 default 使用全部默认值。</param>
        public static async void ShowTip(string text, TipConfig config = default)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // 使用默认值填充未设置的字段
            var finalConfig = GetOrDefault(config);

            // 确保容器存在
            EnsureContainer();

            if (_tipContainer == null)
            {
                Debug.LogError("[UITipManager] Tip container is null. Ensure UIManager is initialized and UIRoot exists.");
                return;
            }

            // 通过 AssetManager 泛型接口直接获取组件实例（首次加载资源，后续复用对象池）
            var tipItem = await XAsset.AssetManager.InstantiateAsync<UITipItem>(TipAssetPath, _tipContainer);
            if (tipItem == null)
            {
                Debug.LogError($"[UITipManager] Failed to instantiate Tip prefab at path: {TipAssetPath}");
                return;
            }

            // 驱动播放，结束后回池
            await tipItem.PlayAsync(text, finalConfig);
            XAsset.AssetManager.DestroyInstance(tipItem.gameObject);
        }

        #endregion

        #region Internal

        /// <summary>
        /// 确保层级容器存在。在 UIRoot 下创建 Layer_Tip 节点。
        /// </summary>
        private static void EnsureContainer()
        {
            if (_tipContainer != null)
                return;

            var uiRoot = GetUIRoot();
            if (uiRoot == null)
                return;

            // 查找已有的容器
            var existing = uiRoot.Find(TipLayerContainerName);
            if (existing != null)
            {
                _tipContainer = existing;
                _tipContainerCanvas = existing.GetComponent<Canvas>();
                return;
            }

            // 创建新容器
            var go = new GameObject(TipLayerContainerName, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(uiRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            // 层级容器自带 Canvas，使用极高的 sorting order
            _tipContainerCanvas = go.AddComponent<Canvas>();
            _tipContainerCanvas.overrideSorting = true;
            _tipContainerCanvas.sortingOrder = TipLayer * 1000;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            _tipContainer = go.transform;
        }

        /// <summary>
        /// 获取 UIRoot Transform。优先通过 UIRootNode，其次通过初始化后的 UIManager。
        /// </summary>
        private static Transform GetUIRoot()
        {
            // 优先使用场景中 UIRootNode
            var node = UIRootNode.FindInScene();
            if (node != null)
                return node.transform;

            Debug.LogWarning("[UITipManager] UIRootNode not found in scene. Cannot show Tip.");
            return null;
        }

        /// <summary>
        /// 用默认值填充未设置的配置字段（因为 struct 不能使用 default 参数带非字面量）。
        /// </summary>
        private static TipConfig GetOrDefault(TipConfig config)
        {
            // 当 Color 为默认黑色（未初始化）时使用白色
            // 注意：Color 的默认值是 (0,0,0,0)，但有含义的默认我们想用白色
            // 使用一个标记检查
            if (config.Color == default)
                config.Color = Color.white;

            if (config.Duration <= 0f)
                config.Duration = 2f;

            // FloatDistance 和 FontSize 的默认值 0 本身就有含义（不飘/默认字号），无需覆盖

            return config;
        }

        #endregion
    }
}