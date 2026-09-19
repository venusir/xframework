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

        private static IUIManager _instance;
        private static bool _instanceInitialized;

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
        /// UI 根节点。未初始化时为 null（属性不抛异常，便于在场景加载早期探测）。
        /// </summary>
        public static Transform UIRoot => _instance?.UIRoot;

        /// <summary>
        /// 当前实例入口，供框架内部与测试使用。
        /// <para>刻意不对外公开为 <c>public Instance</c>：本仓库所有静态服务门面都不暴露实例属性，
        /// 且一旦暴露，<see cref="IUIManager"/> 成员的增加会自动成为公开 API，跳过评审。</para>
        /// </summary>
        internal static IUIManager Current => _instance;

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

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                Update(deltaTime, time);
                return UpdateTier.Tier0;
            }
        }

        /// <summary>
        /// 非零档位的驱动器：只驱动该档位桶里的面板，自身按该档位的周期被派发。
        /// </summary>
        private sealed class LodDriver : IUpdateable
        {
            private readonly UIManagerImpl _impl;
            private readonly UpdateTier _lod;

            public LodDriver(UIManagerImpl impl, UpdateTier lod)
            {
                _impl = impl;
                _lod = lod;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
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
        private static void OnLodDemandChanged(UpdateTier lod)
        {
            if (lod == UpdateTier.Tier0)
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
                UpdateManager.Register(driver, depth: 0, initialTier: lod);
            }
            else if (_lodDrivers.TryGetValue(tier, out var existing))
            {
                UpdateManager.Unregister(existing);
                _lodDrivers.Remove(tier);
            }
        }

        #region Panel

        /// <summary>
        /// Panel subsystem entry points.
        /// </summary>
        public static class Panel
        {

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
                    return _instance.CloseAllAsync(immediate, cancellationToken);
                }



                /// <inheritdoc cref="IUIManager.OpenCount"/>
                public static int OpenCount
                {
                    get
                    {
                        EnsureGlobalInitialized();
                        return _instance.OpenCount;
                    }
                }

                /// <inheritdoc cref="IUIManager.IsAnyOpen"/>
                public static bool IsAnyOpen
                {
                    get
                    {
                        EnsureGlobalInitialized();
                        return _instance.IsAnyOpen;
                    }
                }

                /// <inheritdoc cref="IUIManager.GetTopPanel"/>
                public static UIPanelBase GetTopPanel()
                {
                    EnsureGlobalInitialized();
                    return _instance.GetTopPanel();
                }

                /// <inheritdoc cref="IUIManager.Panels"/>
                public static IReadOnlyList<UIPanelBase> Panels
                {
                    get
                    {
                        EnsureGlobalInitialized();
                        return _instance.Panels;
                    }
                }

                /// <inheritdoc cref="IUIManager.CopyPanels"/>
                public static int CopyPanels(List<UIPanelBase> buffer)
                {
                    EnsureGlobalInitialized();
                    return _instance.CopyPanels(buffer);
                }

                /// <inheritdoc cref="IUIManager.CopyPanelsInLayer"/>
                public static int CopyPanelsInLayer(int layer, List<UIPanelBase> buffer)
                {
                    EnsureGlobalInitialized();
                    return _instance.CopyPanelsInLayer(layer, buffer);
                }



                /// <inheritdoc cref="IUIManager.PreloadAsync{T}"/>
                public static UniTask PreloadAsync<T>(string assetPath, CancellationToken cancellationToken = default)
                    where T : UIPanelBase
                {
                    EnsureGlobalInitialized();
                    return _instance.PreloadAsync<T>(assetPath, cancellationToken);
                }

                /// <inheritdoc cref="IUIManager.ForgetPreload{T}"/>
                public static void ForgetPreload<T>() where T : UIPanelBase
                {
                    EnsureGlobalInitialized();
                    _instance.ForgetPreload<T>();
                }

                /// <inheritdoc cref="IUIManager.ClearPreloads"/>
                public static void ClearPreloads()
                {
                    EnsureGlobalInitialized();
                    _instance.ClearPreloads();
                }

                /// <inheritdoc cref="IUIManager.UnloadPanelAssetAsync"/>
                public static UniTask<bool> UnloadPanelAssetAsync(string assetPath,
                    CancellationToken cancellationToken = default)
                {
                    EnsureGlobalInitialized();
                    return _instance.UnloadPanelAssetAsync(assetPath, cancellationToken);
                }



                /// <inheritdoc cref="IUIManager.BringToFront"/>
                public static void BringToFront(UIPanelBase panel)
                {
                    EnsureGlobalInitialized();
                    _instance.BringToFront(panel);
                }



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


        }

        #endregion

        #region Stack

        /// <summary>
        /// Stack subsystem entry points.
        /// </summary>
        public static class Stack
        {

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


        }

        #endregion

        #region Mask

        /// <summary>
        /// Mask subsystem entry points.
        /// </summary>
        public static class Mask
        {

                /// <inheritdoc cref="IUIManager.ShowMask(UIMaskStyle, UIPanelBase)"/>
                public static UIMaskHandle Show(UIMaskStyle style, UIPanelBase owner = null)
                {
                    EnsureGlobalInitialized();
                    return _instance.ShowMask(style, owner);
                }

                /// <inheritdoc cref="IUIManager.ShowMask(int, float, bool)"/>
                public static UIMaskHandle Show(int maskLayer = UILayers.Mask, float alpha = 0.5f,
                    bool clickToClose = false)
                {
                    EnsureGlobalInitialized();
                    return _instance.ShowMask(maskLayer, alpha, clickToClose);
                }

                /// <inheritdoc cref="IUIManager.SetMaskClickToClose"/>
                public static void SetClickToClose(bool clickToClose)
                {
                    EnsureGlobalInitialized();
                    _instance.SetMaskClickToClose(clickToClose);
                }

                /// <inheritdoc cref="IUIManager.HideMask"/>
                public static void Hide()
                {
                    EnsureGlobalInitialized();
                    _instance.HideMask();
                }

                /// <inheritdoc cref="IUIManager.IsMaskShowing"/>
                public static bool IsShowing
                {
                    get
                    {
                        EnsureGlobalInitialized();
                        return _instance.IsMaskShowing;
                    }
                }


        }

        #endregion

        #region Tip

        /// <summary>
        /// Tip subsystem entry points.
        /// </summary>
        public static class Tip
        {

                /// <inheritdoc cref="IUIManager.ShowTipAsync"/>
                public static UniTask ShowAsync(string text, TipConfig config = default,
                    CancellationToken cancellationToken = default)
                {
                    EnsureGlobalInitialized();
                    return _instance.ShowTipAsync(text, config, cancellationToken);
                }

                /// <inheritdoc cref="IUIManager.SetTipProvider"/>
                public static void SetProvider(IUITipProvider provider)
                {
                    EnsureGlobalInitialized();
                    _instance.SetTipProvider(provider);
                }


        }

        #endregion

        #region Hud

        /// <summary>
        /// Hud subsystem entry points.
        /// </summary>
        public static class Hud
        {

                /// <inheritdoc cref="IUIManager.ShowHudAsync{T}"/>
                public static UniTask<T> Attach<T>(Transform target, string assetPath, Vector2? offset = null,
                    CancellationToken cancellationToken = default) where T : UIHudItem
                {
                    EnsureGlobalInitialized();
                    return _instance.ShowHudAsync<T>(target, assetPath, offset, cancellationToken);
                }

                /// <inheritdoc cref="IUIManager.HideHud"/>
                public static void Detach(Transform target)
                {
                    EnsureGlobalInitialized();
                    _instance.HideHud(target);
                }

                /// <inheritdoc cref="IUIManager.SetHudProvider"/>
                public static void SetProvider(IUiHudProvider provider)
                {
                    EnsureGlobalInitialized();
                    _instance.SetHudProvider(provider);
                }


        }

        #endregion

        #region Layer

        /// <summary>
        /// Layer subsystem entry points.
        /// </summary>
        public static class Layer
        {

                /// <summary>
                /// 显示 / 隐藏整个层级。
                /// <para>此前这两个方法只存在于内部实现上，<strong>连 <see cref="IUIManager"/> 都没有</strong>，
                /// 而门面既无转发、也无实例属性——第三方虽能在文档里读到它们，实际完全不可达。</para>
                /// </summary>
                /// <param name="layer">目标层级。</param>
                /// <param name="visible">是否显示。</param>
                public static void SetVisibility(int layer, bool visible)
                {
                    EnsureGlobalInitialized();
                    _instance.SetLayerVisibility(layer, visible);
                }

                /// <summary>
                /// 启用 / 禁用整个层级的交互。
                /// <para>层的整体开关优先于单个面板的焦点状态：禁用后，后续的焦点变化与重新打开
                /// 都不会把面板的射线重新打开。</para>
                /// </summary>
                /// <param name="layer">目标层级。</param>
                /// <param name="interactive">是否允许交互。</param>
                public static void SetInteractive(int layer, bool interactive)
                {
                    EnsureGlobalInitialized();
                    _instance.SetLayerInteractive(layer, interactive);
                }


        }

        #endregion

        #region Diagnostic

        /// <summary>
        /// Diagnostic subsystem entry points.
        /// </summary>
        public static class Diagnostic
        {

                /// <inheritdoc cref="IUIManager.GetState"/>
                public static UIStateSnapshot GetState()
                {
                    EnsureGlobalInitialized();
                    return _instance.GetState();
                }

                /// <inheritdoc cref="IUIManager.DumpState"/>
                public static string DumpState()
                {
                    EnsureGlobalInitialized();
                    return _instance.DumpState();
                }

                /// <summary>
                /// 已注册的非零档位驱动器数量。用不到分档时恒为 0，便于确认惰性注册确实在生效。
                /// </summary>
                public static int LodDriverCount => _lodDrivers.Count;


        }

        #endregion

        #region Events

        /// <summary>
        /// Events subsystem entry points.
        /// </summary>
        public static class Events
        {

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


        }

        #endregion

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
        }

        private static void EnsureGlobalInitialized()
        {
            if (!_instanceInitialized || _instance == null)
                throw new InvalidOperationException(
                    "[UIManager] UIManager 尚未初始化。请先调用 UIManager.Initialize(uiRoot) 完成初始化。");
        }

    }
}