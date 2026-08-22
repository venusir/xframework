using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XReactive;
using XFramework.XUI.Controller;
using XFramework.XUI.Data;
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
        /// Tip 提供者（默认使用 <see cref="UITipManagerImpl"/>，可通过 <see cref="SetTipProvider"/> 替换）。
        /// </summary>
        private static IUITipProvider _tipProvider;

        /// <summary>
        /// HUD 提供者（默认使用 <see cref="UIHudManagerImpl"/>，可通过 <see cref="SetHudProvider"/> 替换）。
        /// </summary>
        private static IUiHudProvider _hudProvider;

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

            // 初始化默认 Tip / HUD Provider
            EnsureTipProvider();
            EnsureHudProvider();
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
            if (_hudProvider != null)
            {
                _hudProvider.DetachAll();
                _hudProvider = null;
            }
            _tipProvider = null;

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
            if (_hudProvider != null)
                _hudProvider.DetachAll();
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

        #region Public API — Event Subscriptions

        /// <summary>
        /// 订阅面板打开事件。
        /// <para>底层复用 <see cref="MessageManager"/>，提供模块归口入口。</para>
        /// </summary>
        /// <param name="handler">面板打开时的回调</param>
        /// <param name="context">生命周期绑定的 MonoBehaviour（可选），传入后可自动取消订阅</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable Subscribe(Action<PanelOpenedMessage> handler, MonoBehaviour context = null)
        {
            var sub = MessageManager.Subscribe(handler);
            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());
            return sub;
        }

        /// <summary>
        /// 订阅面板关闭事件。
        /// <para>底层复用 <see cref="MessageManager"/>，提供模块归口入口。</para>
        /// </summary>
        /// <param name="handler">面板关闭时的回调</param>
        /// <param name="context">生命周期绑定的 MonoBehaviour（可选），传入后可自动取消订阅</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable Subscribe(Action<PanelClosedMessage> handler, MonoBehaviour context = null)
        {
            var sub = MessageManager.Subscribe(handler);
            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());
            return sub;
        }

        /// <summary>
        /// 订阅全部面板关闭事件。
        /// <para>底层复用 <see cref="MessageManager"/>，提供模块归口入口。</para>
        /// </summary>
        /// <param name="handler">全部面板关闭时的回调</param>
        /// <param name="context">生命周期绑定的 MonoBehaviour（可选），传入后可自动取消订阅</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable Subscribe(Action<AllPanelsClosedMessage> handler, MonoBehaviour context = null)
        {
            var sub = MessageManager.Subscribe(handler);
            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());
            return sub;
        }

        #endregion

        #region Public API — Update

        /// <inheritdoc cref="IUIManager.Update"/>
        public static void Update()
        {
            EnsureGlobalInitialized();
            _instance.Update();
            if (_hudProvider != null)
                _hudProvider.Update();
        }

        #endregion

        #region Public API — Tip

        /// <summary>
        /// 显示一个临时提示文本（Tip）。
        /// <para>通过 <see cref="TipConfig"/> 配置显示行为：世界坐标定位、颜色、持续时长、上飘距离、字号。</para>
        /// <para>内部自动管理实例化和回池，无需手动关闭。可直接调用：<c>UIManager.ShowTip("-10", new TipConfig { WorldPos = enemyPos, Color = Color.red });</c></para>
        /// <para>可通过 <see cref="SetTipProvider"/> 注入自定义 Tip 实现。</para>
        /// </summary>
        /// <param name="text">显示文字。</param>
        /// <param name="config">显示配置。传 default 使用全部默认值（屏幕居中、白色、2秒、不飘动）。</param>
        public static void ShowTip(string text, TipConfig config = default)
        {
            EnsureTipProvider();
            _tipProvider.ShowTip(text, config);
        }

        /// <summary>
        /// 设置自定义 Tip 提供者。传入 null 则恢复默认 <see cref="UITipManagerImpl"/>。
        /// <para>需要在 <see cref="Initialize"/> 后调用。</para>
        /// </summary>
        /// <param name="provider">自定义 Tip 提供者，或 null 恢复默认。</param>
        public static void SetTipProvider(IUITipProvider provider)
        {
            if (provider == null)
            {
                var defaultProvider = new UITipManagerImpl();
                if (_instanceInitialized && _instance?.UIRoot != null)
                    defaultProvider.SetUIRoot(_instance.UIRoot);
                _tipProvider = defaultProvider;
            }
            else
            {
                _tipProvider = provider;
            }
        }

        #endregion

        #region Public API — HUD（世界空间 HUD）

        /// <summary>
        /// 为 3D 目标附加一个 HUD（例如 NPC/怪物头顶的名字、血条）。
        /// <para>HUD 每帧自动跟随 <paramref name="target"/> 的屏幕位置，当 target 为 null 或目标丢失时自动回收。</para>
        /// <para>同一个 target 同时只能绑定一个 HUD，重复调用会先 Detach 旧的。</para>
        /// <para>HUD 预制体由第三方自由设计，只需挂载继承 <see cref="UIHudItem"/> 的脚本即可。</para>
        /// <para>可通过 <see cref="SetHudProvider"/> 注入自定义 HUD 实现。</para>
        /// </summary>
        /// <typeparam name="T">HUD 类型（继承 <see cref="UIHudItem"/>）。</typeparam>
        /// <param name="target">要跟随的 3D 目标 Transform。</param>
        /// <param name="assetPath">HUD 预制体的 YooAsset 地址。</param>
        /// <param name="offset">屏幕坐标偏移（像素）。例如 (0, 80) 将 HUD 移到目标头顶上方。</param>
        /// <returns>附加的 HUD 实例。如果初始化未完成或加载失败则返回 null。</returns>
        public static UniTask<T> ShowHud<T>(Transform target, string assetPath, Vector2? offset = null)
            where T : UIHudItem
        {
            EnsureHudProvider();
            return _hudProvider.AttachAsync<T>(target, assetPath, offset);
        }

        /// <summary>
        /// 分离指定目标绑定的 HUD。
        /// <para>HUD 会自动回池，无需手动控制生命周期。</para>
        /// </summary>
        /// <param name="target">3D 目标 Transform。如果传入 null 则不执行任何操作。</param>
        public static void HideHud(Transform target)
        {
            if (_hudProvider != null)
                _hudProvider.Detach(target);
        }

        /// <summary>
        /// 设置自定义 HUD 提供者。传入 null 则恢复默认 <see cref="UIHudManagerImpl"/>。
        /// <para>需要在 <see cref="Initialize"/> 后调用。</para>
        /// </summary>
        /// <param name="provider">自定义 HUD 提供者，或 null 恢复默认。</param>
        public static void SetHudProvider(IUiHudProvider provider)
        {
            if (provider == null)
            {
                var defaultProvider = new UIHudManagerImpl();
                if (_instanceInitialized && _instance?.UIRoot != null)
                    defaultProvider.SetUIRoot(_instance.UIRoot);
                _hudProvider = defaultProvider;
            }
            else
            {
                _hudProvider = provider;
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
                    "[UIManager] UIManager 尚未初始化。请先调用 UIManager.Initialize(uiRoot) 完成初始化。");
        }

        /// <summary>
        /// 确保 Tip 提供者已创建（懒初始化）。首次调用时使用默认实现。
        /// </summary>
        private static void EnsureTipProvider()
        {
            if (_tipProvider != null)
                return;

            _tipProvider = new UITipManagerImpl();
            if (_instanceInitialized && _instance?.UIRoot != null)
                _tipProvider.SetUIRoot(_instance.UIRoot);
        }

        /// <summary>
        /// 确保 HUD 提供者已创建（懒初始化）。首次调用时使用默认实现。
        /// </summary>
        private static void EnsureHudProvider()
        {
            if (_hudProvider != null)
                return;

            _hudProvider = new UIHudManagerImpl();
            if (_instanceInitialized && _instance?.UIRoot != null)
                _hudProvider.SetUIRoot(_instance.UIRoot);
        }

        #endregion
    }
}