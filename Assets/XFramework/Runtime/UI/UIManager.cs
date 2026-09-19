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
        /// 测试钩子：面板实例来源工厂。默认创建 <see cref="AssetPanelFactory"/>；测试注入假实现，
        /// 以在未初始化 YooAsset 的环境下打开真实面板。
        /// <para>生产代码不应设置它。与 <c>AssetManager.ImplFactory</c> 同形。</para>
        /// </summary>
        internal static Func<IUIPanelFactory> PanelFactoryFactory;

        /// <summary>
        /// 注册到 <see cref="UpdateManager"/> 的每帧驱动器：把面板 / HUD 的每帧更新并入统一调度。
        /// <para>原先靠场景里的 <see cref="UIRootNode.Update"/> 驱动，于是这条通路既不受档位降频、
        /// 也不受 <see cref="UpdateManager.Pause"/> 控制，面板与其它模块的暂停语义还是两套。</para>
        /// </summary>
        private static IUpdateable _frameDriver;

        /// <summary>
        /// 非零档位的驱动器。key: 档位序号。
        /// <para>懒注册：只有该档真的出现了面板才向 <see cref="UpdateManager"/> 注册，桶重新变空即注销。
        /// 用不到的档位不占调度器条目，初始化后调度器里仍只有 <see cref="_frameDriver"/> 一个。</para>
        /// </summary>
        private static readonly Dictionary<int, IUpdateable> _tierDrivers
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

            // 测试钩子消费即清：它是一次性的注入点，留着只会让「设置过钩子但没走 Destroy」的
            // 那一轮把假工厂留给下一个调用方的 Initialize（PlayMode 下所有用例共享一个 player
            // 实例，这种跨 fixture 泄漏一旦发生就是静默的）
            var panelFactoryFactory = PanelFactoryFactory;
            PanelFactoryFactory = null;

            var impl = new UIManagerImpl();
            impl.TierDemandChanged = OnTierDemandChanged;
            impl.Initialize(uiRoot, panelFactoryFactory?.Invoke());

            // 如果传入了自定义控制器，立即设置
            if (controller != null)
                impl.SetController(controller);

            _instance = impl;
            _instanceInitialized = true;

            // 每帧驱动并入统一调度：可在 Initialize 之后被档位降频、被 Pause 统一暂停，
            // 也不再要求场景里必须存在 UIRootNode
            EnsureFrameDriverRegistered();
        }

        /// <summary>
        /// 确保每帧驱动器已注册。生命周期两条入口（<see cref="Initialize"/> 与
        /// <see cref="SetInstance"/>）共用，重复调用无副作用。
        /// <para><b>为什么注入实例也必须有驱动器</b>：驱动器只调 <see cref="Update"/>，而
        /// <see cref="Update"/> 转发给「当前实例」，与实现类型无关——所以它对注入的自定义
        /// <see cref="IUIManager"/> 同样成立。此前只有 <see cref="Initialize"/> 注册驱动器，
        /// 于是走 <see cref="SetInstance"/> 注入之后面板、HUD、Tip 全都没有人来驱动，
        /// 而 <see cref="Update"/> 的文档还写着「不需要自行调用」。</para>
        /// </summary>
        private static void EnsureFrameDriverRegistered()
        {
            if (_frameDriver != null)
                return;

            _frameDriver = new FrameDriver();
            UpdateManager.Register(_frameDriver, order: 0);
        }

        /// <summary>
        /// 设置外部已创建的实例作为全局管理器。
        /// <para>适用于依赖注入或单元测试场景。</para>
        /// </summary>
        public static void SetInstance(IUIManager manager)
        {
            _instance = manager ?? throw new ArgumentNullException(nameof(manager));
            _instanceInitialized = true;

            // 注入的实例同样要有人来驱动。缺了这一步，注入之后面板/HUD/Tip 全都静止，
            // 且驱动类是 private sealed、外部无从补注册——只能靠门面自己接上。
            EnsureFrameDriverRegistered();
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

            foreach (var driver in _tierDrivers.Values)
                UpdateManager.Unregister(driver);

            _tierDrivers.Clear();

            try
            {
                _instance?.Dispose();
            }
            finally
            {
                // 状态复位必须在 finally 里：Dispose 抛异常时若跳过这两行，门面会停在
                // 「IsInitialized 仍为 true、驱动器却已全部摘掉」的半死状态——此后没人再驱动它，
                // 而 IsInitialized 会一直声称没事。
                //
                // 刻意不把 _instance 提前摘掉再 Dispose：Dispose 会走到用户代码
                // （面板的 OnPoolRecycle、provider 的 DetachAll），那期间若有人调门面方法，
                // 提前摘掉会让它从「按半拆状态执行」变成「抛尚未初始化」——那是换一种坏法，
                // 不是修好。
                _instance = null;
                _instanceInitialized = false;

                // 测试钩子随实例一起复位：否则一个 fixture 注入的假工厂会污染后续 fixture 的 Initialize
                PanelFactoryFactory = null;
            }
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
        private sealed class TierDriver : IUpdateable
        {
            private readonly UIManagerImpl _impl;
            private readonly UpdateTier _tier;

            public TierDriver(UIManagerImpl impl, UpdateTier tier)
            {
                _impl = impl;
                _tier = tier;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                _impl.DriveTier(_tier, deltaTime, time);

                // 恒定返回自身档位：面板跑哪一档由各自的 UpdateTier 决定，
                // 驱动器不该因系统繁忙自行漂移（那是面板的声明，不是调度器的推断）
                return _tier;
            }
        }

        /// <summary>
        /// 档位需求变化：按需注册 / 注销该档的驱动器。Tier0 由 <see cref="FrameDriver"/> 承载，跳过。
        /// </summary>
        private static void OnTierDemandChanged(UpdateTier tier)
        {
            if (tier == UpdateTier.Tier0)
                return;

            // 分档只有 UIManagerImpl 能驱动——档位需求由它读面板的 UpdateTier 上报。
            // 注入自定义实现时退化为「只有每帧档」：每帧驱动器仍在（见 EnsureFrameDriverRegistered），
            // 只是没有分档驱动器，这也正是本方法存在的意义。
            if (!(_instance is UIManagerImpl impl))
                return;

            int tierIndex = (int)tier;

            if (impl.HasPanelsAtTier(tier))
            {
                if (_tierDrivers.ContainsKey(tierIndex))
                    return;

                var driver = new TierDriver(impl, tier);
                _tierDrivers[tierIndex] = driver;
                UpdateManager.Register(driver, order: 0, initialTier: tier);
            }
            else if (_tierDrivers.TryGetValue(tierIndex, out var existing))
            {
                UpdateManager.Unregister(existing);
                _tierDrivers.Remove(tierIndex);
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
            return _instance.CloseAllAsync(immediate, cancellationToken);
        }

        #endregion

        #region Public API — Diagnostics

        // 本区与「Query」「Stack Navigation 的 CanGoBack」「Modal Mask 的 IsMaskShowing」里的只读成员
        // 一律不调 EnsureGlobalInitialized：它们回答的是「现在有没有面板/遮罩」这类探测问题，
        // 本就该能在 Initialize 之前回答，IUIManager 的文档与 UIManagerImpl 的实现都以此为契约
        // （「未初始化时返回 0，不抛异常，便于在场景加载早期探测」）。此前门面统一套了守卫，
        // 于是 UIStateWindow 这类调用方必须先判 IsInitialized 才敢问一句「现在什么样」。
        // 操作类成员（Open/Close/Push/ShowMask/…）仍然照抛——那才是「还没准备好就用」的真实错误。

        /// <inheritdoc cref="IUIManager.GetState"/>
        public static UIStateSnapshot GetState()
        {
            return _instance != null ? _instance.GetState() : default;
        }

        /// <inheritdoc cref="IUIManager.DumpState"/>
        public static string DumpState()
        {
            return _instance != null ? _instance.DumpState() : "(UIManager 尚未初始化)";
        }

        /// <summary>
        /// 已注册的非零档位驱动器数量。用不到分档时恒为 0，便于确认惰性注册确实在生效。
        /// </summary>
        public static int TierDriverCount => _tierDrivers.Count;

        #endregion

        #region Public API — Query

        /// <inheritdoc cref="IUIManager.OpenCount"/>
        public static int OpenCount => _instance != null ? _instance.OpenCount : 0;

        /// <inheritdoc cref="IUIManager.IsAnyOpen"/>
        public static bool IsAnyOpen => _instance != null && _instance.IsAnyOpen;

        /// <inheritdoc cref="IUIManager.GetTopPanel"/>
        public static UIPanelBase GetTopPanel()
        {
            EnsureGlobalInitialized();
            return _instance.GetTopPanel();
        }

        /// <inheritdoc cref="IUIManager.Panels"/>
        public static IReadOnlyList<UIPanelBase> Panels
        {
            // 空视图而非 null：探测型读接口不该逼调用方先判 IsInitialized 再判 null
            // （Array.Empty 是缓存的单例，不产生分配）
            get { return _instance != null ? _instance.Panels : Array.Empty<UIPanelBase>(); }
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

        #endregion

        #region Public API — Layer

        /// <summary>
        /// 显示 / 隐藏整个层级。
        /// <para>此前这两个方法只存在于内部实现上，<strong>连 <see cref="IUIManager"/> 都没有</strong>，
        /// 而门面既无转发、也无实例属性——第三方虽能在文档里读到它们，实际完全不可达。</para>
        /// </summary>
        /// <param name="layer">目标层级。</param>
        /// <param name="visible">是否显示。</param>
        public static void SetLayerVisibility(int layer, bool visible)
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
        public static void SetLayerInteractive(int layer, bool interactive)
        {
            EnsureGlobalInitialized();
            _instance.SetLayerInteractive(layer, interactive);
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
        public static bool CanGoBack => _instance != null && _instance.CanGoBack;

        #endregion

        #region Public API — Modal Mask

        /// <inheritdoc cref="IUIManager.ShowMask(UIMaskStyle, UIPanelBase)"/>
        public static UIMaskHandle ShowMask(UIMaskStyle style, UIPanelBase owner = null)
        {
            EnsureGlobalInitialized();
            return _instance.ShowMask(style, owner);
        }

        /// <inheritdoc cref="IUIManager.ShowMask(int, float, bool)"/>
        public static UIMaskHandle ShowMask(int maskLayer = UILayers.Mask, float alpha = 0.5f,
            bool clickToClose = false)
        {
            EnsureGlobalInitialized();
            return _instance.ShowMask(maskLayer, alpha, clickToClose);
        }

        /// <inheritdoc cref="IUIManager.SetMaskClickToClose"/>
        public static void SetMaskClickToClose(bool clickToClose)
        {
            EnsureGlobalInitialized();
            _instance.SetMaskClickToClose(clickToClose);
        }

        /// <inheritdoc cref="IUIManager.HideMask"/>
        public static void HideMask()
        {
            EnsureGlobalInitialized();
            _instance.HideMask();
        }

        /// <inheritdoc cref="IUIManager.IsMaskShowing"/>
        public static bool IsMaskShowing => _instance != null && _instance.IsMaskShowing;

        #endregion

        #region Public API — Preload & Cache

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
        /// <para><b>不需要自行调用</b>：<see cref="Initialize(Transform, IUIController)"/> 与
        /// <see cref="SetInstance"/> 都会把它注册进 <see cref="UpdateManager"/> 的统一调度
        /// （可被档位降频、可被 <see cref="UpdateManager.Pause"/> 统一暂停）。保留公开是因为测试与
        /// 自定义驱动方仍可能需要手动推进。</para>
        /// <para><b>手动调用会与驱动器叠加</b>：驱动器不会因为有人手动调用而让位，一次手动调用
        /// 加一次派发就是每帧驱动两遍（面板拿到两倍步进）。除测试与自定义驱动方外不要调用它。</para>
        /// </summary>
        public static void Update(float deltaTime, float time)
        {
            EnsureGlobalInitialized();
            _instance.Update(deltaTime, time);
        }

        #endregion

        #region Public API — Tip

        /// <inheritdoc cref="IUIManager.ShowTipAsync"/>
        public static UniTask ShowTipAsync(string text, TipConfig config = default,
            CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();
            return _instance.ShowTipAsync(text, config, cancellationToken);
        }

        /// <inheritdoc cref="IUIManager.SetTipProvider"/>
        public static void SetTipProvider(IUITipProvider provider)
        {
            EnsureGlobalInitialized();
            _instance.SetTipProvider(provider);
        }

        #endregion

        #region Public API — HUD（世界空间 HUD）

        /// <inheritdoc cref="IUIManager.ShowHudAsync{T}"/>
        public static UniTask<T> ShowHudAsync<T>(Transform target, string assetPath, Vector2? offset = null,
            CancellationToken cancellationToken = default) where T : UIHudItem
        {
            EnsureGlobalInitialized();
            return _instance.ShowHudAsync<T>(target, assetPath, offset, cancellationToken);
        }

        /// <inheritdoc cref="IUIManager.HideHud"/>
        public static void HideHud(Transform target)
        {
            EnsureGlobalInitialized();
            _instance.HideHud(target);
        }

        /// <inheritdoc cref="IUIManager.SetHudProvider"/>
        public static void SetHudProvider(IUiHudProvider provider)
        {
            EnsureGlobalInitialized();
            _instance.SetHudProvider(provider);
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

        #endregion
    }
}