using UnityEngine;
using XFramework.XUI.View;
using XFramework.XAsset;

namespace XFramework.XUI
{
    /// <summary>
    /// Tip 管理器默认实现。实现 <see cref="IUITipProvider"/> 接口。
    /// <para>负责 Tip 预制体的实例化、层级容器的管理、生命周期调度。</para>
    /// <para>Tip 实例通过 <see cref="XAsset.AssetManager"/> 获取和回池，不自行维护对象池。</para>
    /// <para>所有 Tip 挂载在 UIRoot 下独立的 Layer_Tip 容器中，使用极高的 sorting order 确保在最顶层显示。</para>
    /// <para>第三方可通过 <see cref="UIManager.SetTipProvider"/> 替换此实现。</para>
    /// </summary>
    internal sealed class UITipManagerImpl : IUITipProvider
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

        #region Fields

        private Transform _tipContainer;
        private Canvas _tipContainerCanvas;
        private Transform _uiRoot;

        #endregion

        #region IUITipProvider

        /// <inheritdoc/>
        public void SetUIRoot(Transform uiRoot)
        {
            _uiRoot = uiRoot;
            // 更换 UIRoot 时重置容器引用
            _tipContainer = null;
            _tipContainerCanvas = null;
        }

        /// <inheritdoc/>
        public async void ShowTip(string text, TipConfig config = default)
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
        private void EnsureContainer()
        {
            if (_tipContainer != null)
                return;

            if (_uiRoot == null)
            {
                Debug.LogWarning("[UITipManager] UIRoot is not set. Call SetUIRoot first.");
                return;
            }

            // 查找已有的容器
            var existing = _uiRoot.Find(TipLayerContainerName);
            if (existing != null)
            {
                _tipContainer = existing;
                _tipContainerCanvas = existing.GetComponent<Canvas>();
                return;
            }

            // 创建新容器
            var go = new GameObject(TipLayerContainerName, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_uiRoot, false);
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
        /// 用默认值填充未设置的配置字段（因为 struct 不能使用 default 参数带非字面量）。
        /// </summary>
        private static TipConfig GetOrDefault(TipConfig config)
        {
            // 当 Color 为默认黑色（未初始化）时使用白色
            if (config.Color == default)
                config.Color = Color.white;

            if (config.Duration <= 0f)
                config.Duration = 2f;

            return config;
        }

        #endregion
    }
}