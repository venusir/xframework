using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XMessage;
using XFramework.XUI.Controller;
using XFramework.XUI.Data;
using XFramework.XUI.View;
using XFramework.XUpdate;


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
        /// 测试钩子：面板实例来源工厂。默认创建 <see cref="AssetPanelFactory"/>；测试注入假实现，
        /// 以在未初始化 YooAsset 的环境下打开真实面板。
        /// <para>生产代码不应设置它。与 <c>AssetManager.ImplFactory</c> 同形。</para>
        /// </summary>
        internal static Func<IUIPanelFactory> PanelFactoryFactory;

        /// <summary>
        /// 注册到 <see cref="UpdateManager"/> 的每帧驱动器：把面板 / HUD 的每帧更新并入统一调度。
        /// <para>原先靠场景里的 <see cref="UIRootNode.Update"/> 驱动，于是这条通路既不受 LOD 降频、
        /// 也不受 <see cref="UpdateManager.Pause"/> 控制，面板与其它模块的暂停语义还是两套。</para>
        /// </summary>
        private static IUpdateable _frameDriver;

        /// <summary>
        /// 非零档位的驱动器。key: 档位序号。
        /// <para>懒注册：只有该档真的出现了面板才向 <see cref="UpdateManager"/> 注册，桶重新变空即注销。
        /// 用不到的档位不占调度器条目，初始化后调度器里仍只有 <see cref="_frameDriver"/> 一个。</para>
        /// </summary>
        private static readonly Dictionary<int, IUpdateable> _lodDrivers
            = new Dictionary<int, IUpdateable>(4);

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
            impl.LodDemandChanged = OnLodDemandChanged;
            impl.Initialize(uiRoot, PanelFactoryFactory?.Invoke());

            // 如果传入了自定义控制器，立即设置
            if (controller != null)
                impl.SetController(controller);

            _instance = impl;
            _instanceInitialized = true;

            // 初始化默认 Tip / HUD Provider
            EnsureTipProvider();
            EnsureHudProvider();

            // 每帧驱动并入统一调度：可在 Initialize 之后被 LOD 降频、被 Pause 统一暂停，
            // 也不再要求场景里必须存在 UIRootNode
            _frameDriver = new FrameDriver();
            UpdateManager.Register(_frameDriver, depth: 0);
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
            // 先摘驱动器再拆实例：否则驱动器可能在实例已释放后仍被派发一次
            if (_frameDriver != null)
            {
                UpdateManager.Unregister(_frameDriver);
                _frameDriver = null;
            }

            foreach (var driver in _lodDrivers.Values)
                UpdateManager.Unregister(driver);

            _lodDrivers.Clear();

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

            // 测试钩子随实例一起复位：否则一个 fixture 注入的假工厂会污染后续 fixture 的 Initialize
            PanelFactoryFactory = null;
        }

        /// <summary>
        /// 每帧驱动器：把 <see cref="Update"/> 接到 <see cref="UpdateManager"/> 上。
        /// <para>同时承载 Tier0 面板与 HUD 层的驱动，故它常驻注册。</para>
        /// </summary>
        private sealed class FrameDriver : IUpdateable
        {
            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateLOD OnUpdate(float deltaTime, float time)
            {
                Update(deltaTime, time);
                return UpdateLOD.Tier0;
            }
        }

        /// <summary>
        /// 非零档位的驱动器：只驱动该档位桶里的面板，自身按该档位的周期被派发。
        /// </summary>
        private sealed class LodDriver : IUpdateable
        {
            private readonly UIManagerImpl _impl;
            private readonly UpdateLOD _lod;

            public LodDriver(UIManagerImpl impl, UpdateLOD lod)
            {
                _impl = impl;
                _lod = lod;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateLOD OnUpdate(float deltaTime, float time)
            {
                _impl.DriveLod(_lod, deltaTime, time);

                // 恒定返回自身档位：面板跑哪一档由各自的 UpdateLod 决定，
                // 驱动器不该因系统繁忙自行漂移（那是面板的声明，不是调度器的推断）
                return _lod;
            }
        }

        /// <summary>
        /// 档位需求变化：按需注册 / 注销该档的驱动器。Tier0 由 <see cref="FrameDriver"/> 承载，跳过。
        /// </summary>
        private static void OnLodDemandChanged(UpdateLOD lod)
        {
            if (lod == UpdateLOD.Tier0)
                return;

            // 注入自定义 IUIManager 时无法驱动分档，退回「只有每帧档」的旧行为
            if (!(_instance is UIManagerImpl impl))
                return;

            int tier = (int)lod;

            if (impl.HasPanelsAtLod(lod))
            {
                if (_lodDrivers.ContainsKey(tier))
                    return;

                var driver = new LodDriver(impl, lod);
                _lodDrivers[tier] = driver;
                UpdateManager.Register(driver, depth: 0, initialLOD: lod);
            }
            else if (_lodDrivers.TryGetValue(tier, out var existing))
            {
                UpdateManager.Unregister(existing);
                _lodDrivers.Remove(tier);
            }
        }

        #endregion

        #region Public API — Basic Panel Management

        /// <inheritdoc cref="IUIManager.OpenAsync{T}"/>
        public static UniTask<T> OpenAsync<T>(string assetPath, int layer = 100, object userData = null,
            CancellationToken cancellationToken = default)
            where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.OpenAsync<T>(assetPath, layer, userData, cancellationToken);
        }

        /// <inheritdoc cref="IUIManager.CloseAsync{T}"/>
        public static UniTask CloseAsync<T>(bool immediate = false, CancellationToken cancellationToken = default)
            where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.CloseAsync<T>(immediate, cancellationToken);
        }

        /// <inheritdoc cref="IUIManager.CloseAsync(UIPanelBase, bool)"/>
        public static UniTask CloseAsync(UIPanelBase panel, bool immediate = false,
            CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.CloseAsync(panel, immediate, cancellationToken);
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
        public static UniTask CloseLayerAsync(int layer, bool immediate = false,
            CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.CloseLayerAsync(layer, immediate, cancellationToken);
        }

        /// <inheritdoc cref="IUIManager.CloseAllAsync"/>
        public static UniTask CloseAllAsync(bool immediate = false, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            if (_hudProvider != null)
                _hudProvider.DetachAll();
            return _instance.CloseAllAsync(immediate, cancellationToken);
        }

        #endregion

        #region Public API — Stack Navigation

        /// <inheritdoc cref="IUIManager.PushAsync{T}"/>
        public static UniTask<T> PushAsync<T>(string assetPath, int layer = 100, object userData = null,
            CancellationToken cancellationToken = default)
            where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.PushAsync<T>(assetPath, layer, userData, cancellationToken);
        }

        /// <inheritdoc cref="IUIManager.PopAsync"/>
        public static UniTask PopAsync(bool immediate = false, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.PopAsync(immediate, cancellationToken);
        }

        /// <inheritdoc cref="IUIManager.PopToAsync{T}"/>
        public static UniTask PopToAsync<T>(bool immediate = false, CancellationToken cancellationToken = default)
            where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.PopToAsync<T>(immediate, cancellationToken);
        }

        /// <inheritdoc cref="IUIManager.PopToRootAsync"/>
        public static UniTask PopToRootAsync(bool immediate = false, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.PopToRootAsync(immediate, cancellationToken);
        }

        /// <inheritdoc cref="IUIManager.GoBackAsync"/>
        public static UniTask GoBackAsync(bool immediate = false, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.GoBackAsync(immediate, cancellationToken);
        }

        /// <inheritdoc cref="IUIManager.CanGoBack"/>
        public static bool CanGoBack
        {
            get
            {
                EnsureGlobalInitialized();
                return _instance.CanGoBack;
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
        public static UniTask PreloadAsync<T>(string assetPath, CancellationToken cancellationToken = default)
            where T : UIPanelBase
        {
            EnsureGlobalInitialized();
            return _instance.PreloadAsync<T>(assetPath, cancellationToken);
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

        /// <summary>
        /// 每帧更新。遍历所有 IsOpen 的面板调用 <see cref="UIPanelBase.OnUpdate"/>，并驱动 HUD 层。
        /// <para><b>不需要自行调用</b>：<see cref="Initialize(Transform, IUIController)"/> 已把它注册进
        /// <see cref="UpdateManager"/> 的统一调度（可被 LOD 降频、可被 <see cref="UpdateManager.Pause"/>
        /// 统一暂停）。保留公开是因为测试与自定义驱动方仍可能需要手动推进一步。</para>
        /// </summary>
        public static void Update(float deltaTime, float time)
        {
            EnsureGlobalInitialized();
            _instance.Update(deltaTime, time);
            if (_hudProvider != null)
                _hudProvider.Update(deltaTime, time);
        }

        #endregion

        #region Public API — Tip

        /// <summary>
        /// 显示一个临时提示文本（Tip）。
        /// <para>通过 <see cref="TipConfig"/> 配置显示行为：世界坐标定位、颜色、持续时长、上飘距离、字号。</para>
        /// <para>内部自动管理实例化和回池，无需手动关闭。可直接调用：<c>UIManager.ShowTipAsync("-10", new TipConfig { WorldPos = enemyPos, Color = Color.red }).Forget();</c></para>
        /// <para>可通过 <see cref="SetTipProvider"/> 注入自定义 Tip 实现。</para>
        /// </summary>
        /// <param name="text">显示文字。</param>
        /// <param name="config">显示配置。传 default 使用全部默认值（屏幕居中、白色、2秒、不飘动）。</param>
        /// <param name="cancellationToken">取消令牌，用于提前终止播放。</param>
        /// <returns>播放结束（或取消）后完成。不关心时用 <c>.Forget()</c>。</returns>
        public static UniTask ShowTipAsync(string text, TipConfig config = default,
            CancellationToken cancellationToken = default)
        {
            EnsureTipProvider();
            return _tipProvider.ShowTipAsync(text, config, cancellationToken);
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
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>附加的 HUD 实例。如果初始化未完成或加载失败则返回 null。</returns>
        public static UniTask<T> ShowHud<T>(Transform target, string assetPath, Vector2? offset = null,
            CancellationToken cancellationToken = default) where T : UIHudItem
        {
            EnsureHudProvider();
            return _hudProvider.AttachAsync<T>(target, assetPath, offset, cancellationToken);
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