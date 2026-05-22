using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.Controller;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// 全局 UI 管理器外观。提供静态方法直接管理 UI 面板。
    /// <para>内部持有 <see cref="IUIManager"/> 实例（<see cref="UIManagerImpl"/>），所有调用委托到该实例。</para>
    /// <para>使用前需调用 <see cref="Initialize"/> 传入 UI 根节点。</para>
    /// <para>层级使用 <see cref="int"/> 类型，数值越大越靠前。推荐在项目中定义常量扩展层级。</para>
    /// </summary>
    public static class UIManager
    {
        #region Static — Global Singleton

        private static IUIManager _instance;
        private static bool _instanceInitialized;

        /// <summary>
        /// 全局 UI 管理器是否已初始化。
        /// </summary>
        public static bool IsInitialized => _instanceInitialized && _instance != null;

        /// <summary>
        /// 初始化全局 UI 管理器，将场景中的 UIRootNode 注册为 UI 根节点。
        /// <para>每个场景只需调用一次。</para>
        /// </summary>
        /// <param name="uiRoot">场景中 UIRootNode 的 Transform。</param>
        /// <param name="controller">自定义 UI 控制器（可选），用于拦截面板打开/关闭逻辑。</param>
        public static void Initialize(Transform uiRoot, IUIController controller = null)
        {
            if (_instanceInitialized)
            {
                Debug.LogWarning("[UIManager] Initialize was called more than once. Ignoring duplicate.");
                return;
            }

            var impl = new UIManagerImpl();
            impl.Initialize(uiRoot);

            // 如果传入了自定义控制器，立即设置
            if (controller != null)
                impl.SetController(controller);

            _instance = impl;
            _instanceInitialized = true;
        }

        /// <summary>
        /// 设置外部已创建的实例作为全局管理器。
        /// <para>适用于依赖注入或单元测试场景。</para>
        /// </summary>
        public static void SetInstance(IUIManager manager)
        {
            _instance = manager ?? throw new ArgumentNullException(nameof(manager));
            _instanceInitialized = true;
        }

        /// <summary>
        /// 销毁全局 UI 管理器，释放所有资源。
        /// </summary>
        public static void Destroy()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }
            _instanceInitialized = false;
        }

        #endregion

        #region Public API — Basic Panel Management

        /// <inheritdoc cref="IUIManager.OpenAsync{T}"/>
        public static UniTask<T> OpenAsync<T>(string assetPath, int layer = 100, object userData = null)
            where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.OpenAsync<T>(assetPath, layer, userData);
        }

        /// <inheritdoc cref="IUIManager.CloseAsync{T}"/>
        public static UniTask CloseAsync<T>(bool immediate = false) where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.CloseAsync<T>(immediate);
        }

        /// <inheritdoc cref="IUIManager.CloseAsync(UIPanelBase, bool)"/>
        public static UniTask CloseAsync(UIPanelBase panel, bool immediate = false)
        {
            EnsureGlobalInitialized();
            return _instance.CloseAsync(panel, immediate);
        }

        /// <inheritdoc cref="IUIManager.IsOpen{T}"/>
        public static bool IsOpen<T>() where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.IsOpen<T>();
        }

        /// <inheritdoc cref="IUIManager.GetPanel{T}"/>
        public static T GetPanel<T>() where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.GetPanel<T>();
        }

        /// <inheritdoc cref="IUIManager.CloseLayerAsync"/>
        public static UniTask CloseLayerAsync(int layer, bool immediate = false)
        {
            EnsureGlobalInitialized();
            return _instance.CloseLayerAsync(layer, immediate);
        }

        /// <inheritdoc cref="IUIManager.CloseAllAsync"/>
        public static UniTask CloseAllAsync(bool immediate = false)
        {
            EnsureGlobalInitialized();
            return _instance.CloseAllAsync(immediate);
        }

        #endregion

        #region Public API — Stack Navigation

        /// <inheritdoc cref="IUIManager.PushAsync{T}"/>
        public static UniTask<T> PushAsync<T>(string assetPath, int layer = 100, object userData = null)
            where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.PushAsync<T>(assetPath, layer, userData);
        }

        /// <inheritdoc cref="IUIManager.PopAsync"/>
        public static UniTask PopAsync(bool immediate = false)
        {
            EnsureGlobalInitialized();
            return _instance.PopAsync(immediate);
        }

        /// <inheritdoc cref="IUIManager.BackToAsync{T}"/>
        public static UniTask BackToAsync<T>(bool immediate = false) where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.BackToAsync<T>(immediate);
        }

        /// <inheritdoc cref="IUIManager.GoBackAsync"/>
        public static UniTask GoBackAsync(bool immediate = false)
        {
            EnsureGlobalInitialized();
            return _instance.GoBackAsync(immediate);
        }

        /// <inheritdoc cref="IUIManager.HasPrevious"/>
        public static bool HasPrevious
        {
            get
            {
                EnsureGlobalInitialized();
                return _instance.HasPrevious;
            }
        }

        #endregion

        #region Public API — Modal Mask

        /// <inheritdoc cref="IUIManager.ShowMask"/>
        public static void ShowMask(int maskLayer = 500, float alpha = 0.5f, bool clickToClose = false)
        {
            EnsureGlobalInitialized();
            _instance.ShowMask(maskLayer, alpha, clickToClose);
        }

        /// <inheritdoc cref="IUIManager.HideMask"/>
        public static void HideMask()
        {
            EnsureGlobalInitialized();
            _instance.HideMask();
        }

        /// <inheritdoc cref="IUIManager.IsMaskShowing"/>
        public static bool IsMaskShowing
        {
            get
            {
                EnsureGlobalInitialized();
                return _instance.IsMaskShowing;
            }
        }

        #endregion

        #region Public API — Preload & Cache

        /// <inheritdoc cref="IUIManager.PreloadAsync{T}"/>
        public static UniTask PreloadAsync<T>(string assetPath) where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.PreloadAsync<T>(assetPath);
        }

        /// <inheritdoc cref="IUIManager.UnloadAsset{T}"/>
        public static void UnloadAsset<T>() where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            _instance.UnloadAsset<T>();
        }

        /// <inheritdoc cref="IUIManager.ClearAssetCache"/>
        public static void ClearAssetCache()
        {
            EnsureGlobalInitialized();
            _instance.ClearAssetCache();
        }

        #endregion

        #region Public API — Sort Order

        /// <inheritdoc cref="IUIManager.GetTopSortingOrder"/>
        public static int GetTopSortingOrder(int layer)
        {
            EnsureGlobalInitialized();
            return _instance.GetTopSortingOrder(layer);
        }

        /// <inheritdoc cref="IUIManager.BringToFront"/>
        public static void BringToFront(UIPanelBase panel)
        {
            EnsureGlobalInitialized();
            _instance.BringToFront(panel);
        }

        #endregion

        #region Public API — Update

        /// <inheritdoc cref="IUIManager.Update"/>
        public static void Update()
        {
            EnsureGlobalInitialized();
            _instance.Update();
        }

        #endregion

        #region Public API — Events

        /// <inheritdoc cref="IUIManager.OnPanelOpened"/>
        public static event Action<Type> OnPanelOpened
        {
            add
            {
                EnsureGlobalInitialized();
                _instance.OnPanelOpened += value;
            }
            remove
            {
                EnsureGlobalInitialized();
                _instance.OnPanelOpened -= value;
            }
        }

        /// <inheritdoc cref="IUIManager.OnPanelClosed"/>
        public static event Action<Type> OnPanelClosed
        {
            add
            {
                EnsureGlobalInitialized();
                _instance.OnPanelClosed += value;
            }
            remove
            {
                EnsureGlobalInitialized();
                _instance.OnPanelClosed -= value;
            }
        }

        /// <inheritdoc cref="IUIManager.OnAllPanelsClosed"/>
        public static event Action OnAllPanelsClosed
        {
            add
            {
                EnsureGlobalInitialized();
                _instance.OnAllPanelsClosed += value;
            }
            remove
            {
                EnsureGlobalInitialized();
                _instance.OnAllPanelsClosed -= value;
            }
        }

        #endregion

        #region Public API — UI Controller

        /// <summary>
        /// 设置自定义 UI 控制器，用于拦截面板打开/关闭流程。
        /// <para>需要在 <see cref="Initialize"/> 后调用。设置为 null 则恢复默认控制器（全部放行）。</para>
        /// </summary>
        /// <param name="controller">自定义控制器实例，或 null 以恢复默认。</param>
        public static void SetController(IUIController controller)
        {
            EnsureGlobalInitialized();
            if (_instance is UIManagerImpl impl)
                impl.SetController(controller);
            else
                Debug.LogWarning(
                    "[UIManager] SetController: Current instance is not UIManagerImpl, controller not set.");
        }

        #endregion

        #region Internal

        private static void EnsureGlobalInitialized()
        {
            if (!_instanceInitialized || _instance == null)
                throw new InvalidOperationException(
                    "UIManager is not initialized. Call UIManager.Initialize(uiRoot) first.");
        }

        #endregion
    }
}