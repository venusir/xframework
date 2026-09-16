using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XLocalization;
using XFramework.XMessage;
using XFramework.XPool;
using XFramework.XUpdate;
using XFramework.XUI.Controller;
using XFramework.XUI.Data;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// <see cref="IUIManager"/> 的默认实现。
    /// <para>维护活动面板字典、面板显示栈、每层交互开关与资源缓存；排序值由 <see cref="UISorting"/> 按栈位推导。</para>
    /// <para>面板实例的创建与回收委托给 <see cref="IUIPanelFactory"/>（默认 <see cref="AssetPanelFactory"/>）。</para>
    /// </summary>
    internal sealed class UIManagerImpl : IUIManager
    {
        #region Fields

        /// <summary>
        /// 活动面板字典。key: 面板类型, value: 面板实例。
        /// </summary>
        private readonly Dictionary<Type, UIPanelBase> _activePanels
            = new Dictionary<Type, UIPanelBase>(8);

        /// <summary>
        /// 面板显示栈（底 → 顶，栈序即显示次序）。与 <see cref="_activePanels"/> 一一对应。
        /// <para>所有打开路径都入栈，<see cref="OpenAsync{T}"/> 也不例外。早先只有 <see cref="PushAsync{T}"/>
        /// 入栈，于是「Open 开主界面 + Push 开二级页」之后栈深恒为 1，PopAsync / CanGoBack /
        /// 遮罩点击关闭会同时失效——那是最常见的用法组合。</para>
        /// </summary>
        private readonly List<UIPanelBase> _stack = new List<UIPanelBase>(8);

        /// <summary>
        /// 每层的交互开关期望值。层被整体禁用交互后，面板获得焦点时不应把 raycaster 重新打开。
        /// </summary>
        private readonly Dictionary<int, bool> _layerInteractive
            = new Dictionary<int, bool>(4);

        /// <summary>
        /// 预加载资源路径缓存。key: 类型, value: assetPath。
        /// <para>标记哪些面板已被预加载到 AssetManager 的对象池中。</para>
        /// </summary>
        private readonly Dictionary<Type, string> _assetCache
            = new Dictionary<Type, string>(8);

        /// <summary>
        /// 遮罩 GameObject 实例（只创建一次，之后复用）。
        /// </summary>
        private GameObject _maskInstance;

        /// <summary>点击关闭所用的 Button，仅在开启该功能时存在。</summary>
        private UnityEngine.UI.Button _maskButton;

        /// <summary>
        /// 遮罩是否启用了点击关闭功能。
        /// </summary>
        private bool _maskClickToClose;

        /// <summary>
        /// 当前持有的遮罩引用。每个 <see cref="ShowMask(UIMaskStyle, UIPanelBase)"/> 对应一项，
        /// 全部释放后才真正隐藏——多个系统各自需要遮罩时不再互相踩。
        /// </summary>
        private readonly List<MaskEntry> _maskEntries = new List<MaskEntry>(4);

        /// <summary>句柄令牌发号器。单调递增，不在释放后回收——令牌复用会让已释放的旧句柄误伤新持有者。</summary>
        private int _nextMaskToken = 1;

        /// <summary>一次遮罩持有。</summary>
        private struct MaskEntry
        {
            /// <summary>句柄令牌。</summary>
            public int Token;

            /// <summary>持有者面板（可为 null）。面板关闭时其持有会被自动释放。</summary>
            public UIPanelBase Owner;
        }

        /// <summary>
        /// UI 控制器。用于拦截面板打开/关闭流程。
        /// <para>默认使用 <see cref="UIDefaultController"/>，可通过 <see cref="SetController"/> 替换。</para>
        /// </summary>
        private IUIController _controller;

        /// <summary>
        /// 面板实例的来源。默认 <see cref="AssetPanelFactory"/>；测试可经
        /// <see cref="UIManager.PanelFactoryFactory"/> 注入假实现。
        /// </summary>
        private IUIPanelFactory _factory;

        /// <summary>
        /// 在途打开记录。key: 面板类型。
        /// <para>同类型并发打开（同帧重复点击、两个系统同时请求同一面板）只实例化一次，
        /// 后来者加入同一次打开并共享结果。此前没有这层去重，第二次 <c>RegisterPanel</c>
        /// 会把第一个实例直接回池，而它的 <c>IsOpen</c> 仍是 true、首个调用方拿到的是池中
        /// 失活引用，<see cref="PanelOpenedMessage"/> 还会发两次。</para>
        /// </summary>
        private readonly Dictionary<Type, InFlightOpen> _opening
            = new Dictionary<Type, InFlightOpen>(4);

        /// <summary>
        /// 面板按各自 <see cref="UIViewBase.UpdateLod"/> 分桶。某一档的驱动器被派发时只驱动本桶，
        /// 于是「用不到每帧的面板」可以声明较低档位降耗，而不必各自写节流。
        /// </summary>
        private readonly List<UIPanelBase>[] _buckets;

        /// <summary>上一次已通知过的「该档是否有面板」，用于只在 0 ↔ 非空 跳变时通知门面。</summary>
        private readonly bool[] _lodDemand;

        /// <summary>
        /// 档位需求变化回调，由门面注入。门面据此懒注册/注销该档的驱动器——
        /// 用不到的档位不占调度器条目。
        /// </summary>
        internal Action<UpdateLOD> LodDemandChanged;

        /// <summary>
        /// 语言变更消息订阅句柄。Dispose 时取消订阅。
        /// </summary>
        private IDisposable _languageChangedSubscription;

        #endregion

        #region Construction

        public UIManagerImpl()
        {
            int tiers = (int)UpdateLOD.Max + 1;
            _buckets = new List<UIPanelBase>[tiers];
            _lodDemand = new bool[tiers];

            for (int i = 0; i < tiers; i++)
                _buckets[i] = new List<UIPanelBase>(4);
        }

        #endregion

        #region Properties

        public bool IsInitialized { get; private set; }

        public Transform UIRoot { get; private set; }

        public bool IsMaskShowing => _maskInstance != null && _maskInstance.activeSelf;

        public bool CanGoBack => _stack.Count > 1;

        #endregion

        #region Initialization

        /// <summary>
        /// 初始化 UI 管理器，设置 UI 根节点。
        /// </summary>
        /// <param name="uiRoot">场景中 UIRootNode 的 Transform。</param>
        /// <param name="factory">面板实例来源；传 null 使用默认的 <see cref="AssetPanelFactory"/>。</param>
        internal void Initialize(Transform uiRoot, IUIPanelFactory factory = null)
        {
            if (uiRoot == null)
                throw new ArgumentNullException(nameof(uiRoot));

            UIRoot = uiRoot;
            IsInitialized = true;

            // 默认使用 UIDefaultController（所有操作直接放行）
            _controller = new UIDefaultController();

            // 默认经 AssetManager 加载与回池
            _factory = factory ?? new AssetPanelFactory();

            // 订阅语言变更消息，自动通知所有已打开面板刷新文本
            _languageChangedSubscription = MessageManager.Subscribe<LanguageChangedMessage>(OnLanguageChangedMessage);
        }

        /// <summary>
        /// 设置 UI 控制器。可在 Initialize 后随时替换。
        /// <para>设置为 null 则恢复默认控制器（全部放行）。</para>
        /// </summary>
        /// <param name="controller">自定义控制器实例，或 null 以恢复默认。</param>
        internal void SetController(IUIController controller)
        {
            _controller = controller ?? new UIDefaultController();
        }

        /// <summary>
        /// 销毁所有内容。面板实例回池由 AssetManager 管理。
        /// </summary>
        public void Dispose()
        {
            // 取消消息订阅
            _languageChangedSubscription?.Dispose();
            _languageChangedSubscription = null;

            // 同步关闭所有面板（回池而非 Destroy）
            var panels = new List<UIPanelBase>(_activePanels.Values);
            foreach (var panel in panels)
            {
                _factory.Release(panel);
            }

            // 在途打开的加入者不能悬着：以 null 结束，否则它们会永远等下去
            foreach (var entry in _opening.Values)
            {
                if (entry.Joiners == null)
                    continue;

                for (int i = 0; i < entry.Joiners.Count; i++)
                    entry.Joiners[i].TrySetResult(null);
            }
            _opening.Clear();

            _activePanels.Clear();
            _stack.Clear();

            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i].Clear();
                _lodDemand[i] = false;
            }

            // 清理缓存（AssetManager 对象池由 AssetManager.Dispose 统一管理）
            _assetCache.Clear();

            // 销毁遮罩。它由 new GameObject 创建、从未经 AssetManager 托管，
            // 故直接销毁即可——早先走 AssetManager.DestroyInstance 会让 Dispose
            // 依赖 AssetManager 存活（后者未初始化时直接抛异常）
            _maskEntries.Clear();
            _maskButton = null;

            if (_maskInstance != null)
            {
                UnityEngine.Object.Destroy(_maskInstance);
                _maskInstance = null;
            }

            UIRoot = null;
            IsInitialized = false;
        }

        #endregion

        #region Basic Panel Management

        public async UniTask<T> OpenAsync<T>(string assetPath, int layer = 100, object userData = null,
            CancellationToken cancellationToken = default) where T : UIPanelBase
        {
            EnsureInitialized();
            var type = typeof(T);

            // 钳制层级：超过上限会撞进遮罩/HUD/Tip 的保留带
            layer = UISorting.ClampPanelLayer(layer);

            // 已打开的面板直接聚焦（不触发 Controller 拦截）
            if (_activePanels.TryGetValue(type, out var existingPanel) && existingPanel != null)
            {
                BringToFront(existingPanel);
                return existingPanel as T;
            }

            // 正在打开：加入同一次打开，共享首个调用者的结果
            if (_opening.TryGetValue(type, out var inFlight))
            {
#if UNITY_EDITOR
                if (inFlight.AssetPath != assetPath)
                {
                    Debug.LogWarning(
                        $"[UIManager] Panel '{type.Name}' is already being opened from '{inFlight.AssetPath}'; " +
                        $"the request for '{assetPath}' will reuse that result.");
                }
#endif
                return await JoinOpeningAsync<T>(inFlight);
            }

            var entry = new InFlightOpen { AssetPath = assetPath };
            _opening[type] = entry;

            UIPanelBase pending = null;
            UIPanelBase opened = null;
            bool committed = false;

            try
            {
                // 取消语义：此点之前（含 Controller 校验与实例化）取消会生效并以
                // OperationCanceledException 上抛，实例由回滚路径还回池子。
                cancellationToken.ThrowIfCancellationRequested();

                // ★ Controller 拦截点：打开前校验
                var canOpen = await _controller.OnBeforeOpenAsync(type, assetPath, layer, userData, cancellationToken);
                if (!canOpen)
                {
                    Debug.LogWarning($"[UIManager] Panel open blocked by Controller: {type.Name}");
                    return null;
                }

                // 实例化面板
                var panel = await InstantiatePanelAsync<T>(assetPath, layer, cancellationToken);
                if (panel == null)
                {
                    Debug.LogError($"[UIManager] Failed to instantiate panel: {type.Name} at path: {assetPath}");
                    return null;
                }

                // 从这里起实例已存在，任何失败都必须把它还回池子
                pending = panel;

                panel.Layer = layer;
                panel.AssetPath = assetPath;

                // 注册（内含按栈位重排 sortingOrder）并打开。
                // 此后取消不再生效：注册已发生，中途放弃会留下半开状态
                RegisterPanel(type, panel);
                await panel.DoOpenAsync(userData);

                // 提交点：打开已完成，此后不再回滚（后续回调失败不应把已打开的面板拆掉）
                committed = true;
                opened = panel;

                // 面板可能在自己的 OnOpen 里就把自己关了（此时它已回池）。那种情况下
                // 「打开完成」并没有发生，故跳过下面两个表达该语义的回调，但仍把实例
                // 返回给调用方——调用方应检查 IsOpen。
                if (!_activePanels.ContainsKey(type))
                    return panel;

                MessageManager.Publish(new PanelOpenedMessage(type));

                // ★ Controller 拦截点：打开后回调
                await _controller.OnAfterOpenAsync(type, panel, userData, cancellationToken);

                return panel;
            }
            catch
            {
                if (!committed && pending != null)
                    RollbackPanel(type, pending);

                throw;
            }
            finally
            {
                SettleOpening(type, entry, opened);
            }
        }

        public async UniTask CloseAsync<T>(bool immediate = false, CancellationToken cancellationToken = default)
            where T : UIPanelBase
        {
            EnsureInitialized();
            var type = typeof(T);

            if (_activePanels.TryGetValue(type, out var panel) && panel != null)
            {
                await ClosePanelInternalAsync(panel, type, immediate, cancellationToken);
            }
        }

        public async UniTask CloseAsync(UIPanelBase panel, bool immediate = false,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            if (panel == null)
                return;

            var type = panel.GetType();
            if (_activePanels.ContainsKey(type))
            {
                await ClosePanelInternalAsync(panel, type, immediate, cancellationToken);
            }
        }

        public bool IsOpen<T>() where T : UIPanelBase
        {
            EnsureInitialized();
            return _activePanels.ContainsKey(typeof(T));
        }

        public T GetPanel<T>() where T : UIPanelBase
        {
            EnsureInitialized();
            var type = typeof(T);
            _activePanels.TryGetValue(type, out var panel);
            return panel as T;
        }

        public async UniTask CloseLayerAsync(int layer, bool immediate = false,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            // 收集指定层级的所有面板（避免遍历中修改字典）
            var toClose = new List<(UIPanelBase panel, Type type)>();
            foreach (var kv in _activePanels)
            {
                if (kv.Value.Layer == layer)
                {
                    toClose.Add((kv.Value, kv.Key));
                }
            }

            foreach (var item in toClose)
            {
                await ClosePanelInternalAsync(item.panel, item.type, immediate, cancellationToken);
            }
        }

        public async UniTask CloseAllAsync(bool immediate = false,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            // 收集所有面板（注意：HUD 的 DetachAll 已由 UIManager.CloseAllAsync 在 facade 层处理）
            var toClose = new List<(UIPanelBase panel, Type type)>();
            foreach (var kv in _activePanels)
            {
                toClose.Add((kv.Value, kv.Key));
            }

            foreach (var item in toClose)
            {
                await ClosePanelInternalAsync(item.panel, item.type, immediate, cancellationToken);
            }

            MessageManager.Publish(new AllPanelsClosedMessage());

            // ★ Controller 拦截点：全部关闭后回调
            await _controller.OnAllPanelsClosedAsync(cancellationToken);
        }

        #endregion

        #region Stack Navigation

        public async UniTask<T> PushAsync<T>(string assetPath, int layer = 100, object userData = null,
            CancellationToken cancellationToken = default) where T : UIPanelBase
        {
            EnsureInitialized();
            var type = typeof(T);

            // 目标已打开：只聚焦，不先模糊栈顶——栈顶可能就是它自己，那样会把
            // 它留在失焦态（IsPaused + Raycaster 关闭）且无人恢复
            if (_activePanels.TryGetValue(type, out var existing) && existing != null)
            {
                BringToFront(existing);
                return existing as T;
            }

            // 模糊当前栈顶面板；打开失败或取消时要把它恢复回来
            var blurred = BlurTopPanel();

            T panel;
            try
            {
                panel = await OpenAsync<T>(assetPath, layer, userData, cancellationToken);
            }
            catch
            {
                // 取消与异常同样要回滚失焦，否则栈顶会永久停在不可交互状态
                UnblurPanel(blurred);
                throw;
            }

            if (panel == null)
                UnblurPanel(blurred);

            return panel;
        }

        public async UniTask PopAsync(bool immediate = false, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            PruneStack();

            // 栈底面板不参与弹出：PopAsync 只退一层
            if (_stack.Count <= 1)
                return;

            var top = _stack[_stack.Count - 1];
            if (await ClosePanelInternalAsync(top, top.GetType(), immediate, cancellationToken))
            {
                FocusTopPanel();
            }
        }

        public async UniTask PopToAsync<T>(bool immediate = false, CancellationToken cancellationToken = default)
            where T : UIPanelBase
        {
            EnsureInitialized();
            PruneStack();

            var target = GetPanel<T>();
            if (target == null)
            {
                Debug.LogWarning($"[UIManager] PopToAsync: Panel '{typeof(T).Name}' is not open.");
                return;
            }

            // 逐个弹出栈顶，直到目标成为栈顶。每次成功关闭恰好移出一项，故必然收敛；
            // 中途被 Controller 拦下则停止级联（栈未变，继续关会与拦截语义矛盾）。
            while (true)
            {
                PruneStack();

                var index = _stack.IndexOf(target);
                if (index < 0 || index >= _stack.Count - 1)
                    break;

                var top = _stack[_stack.Count - 1];
                if (!await ClosePanelInternalAsync(top, top.GetType(), immediate, cancellationToken))
                    break;
            }

            FocusTopPanel();
        }

        public async UniTask PopToRootAsync(bool immediate = false, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            PruneStack();

            // 只保留最早打开的那一个（栈底）
            while (true)
            {
                PruneStack();

                if (_stack.Count <= 1)
                    break;

                var top = _stack[_stack.Count - 1];
                if (!await ClosePanelInternalAsync(top, top.GetType(), immediate, cancellationToken))
                    break;
            }

            FocusTopPanel();
        }

        public async UniTask GoBackAsync(bool immediate = false, CancellationToken cancellationToken = default)
        {
            await PopAsync(immediate, cancellationToken);
        }

        #endregion

        #region Modal Mask

        public UIMaskHandle ShowMask(UIMaskStyle style, UIPanelBase owner = null)
        {
            EnsureInitialized();

            EnsureMaskInstance();
            ApplyMaskStyle(style);
            ApplyMaskClickToClose(style.ClickToClose);

            int token = _nextMaskToken++;
            _maskEntries.Add(new MaskEntry { Token = token, Owner = owner });

            _maskInstance.SetActive(true);
            return new UIMaskHandle(this, token);
        }

        public UIMaskHandle ShowMask(int maskLayer = UILayers.Mask, float alpha = 0.5f, bool clickToClose = false)
        {
            return ShowMask(new UIMaskStyle(maskLayer, new Color(0f, 0f, 0f, alpha), clickToClose));
        }

        public void HideMask()
        {
            // 不接受句柄的调用方走这条路：清掉全部引用并隐藏
            _maskEntries.Clear();

            if (_maskInstance != null)
                _maskInstance.SetActive(false);
        }

        public void SetMaskClickToClose(bool clickToClose)
        {
            EnsureInitialized();

            // 样式属于每一次 ShowMask 调用，本方法只用于「正在显示时改」。
            // 没有遮罩时无对象可改，出声而不是静默丢弃。
            if (_maskInstance == null)
            {
                Debug.LogWarning(
                    "[UIManager] SetMaskClickToClose: 遮罩尚未显示，本次调用无效。" +
                    "请在 ShowMask 的 UIMaskStyle 里指定 ClickToClose。");
                return;
            }

            ApplyMaskClickToClose(clickToClose);
        }

        /// <summary>
        /// 释放指定面板持有的全部遮罩引用。面板关闭时调用，于是「面板自己开的遮罩」
        /// 不需要调用方记得配对关闭。
        /// </summary>
        private void ReleaseMasksOwnedBy(UIPanelBase panel)
        {
            for (int i = _maskEntries.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_maskEntries[i].Owner, panel))
                    _maskEntries.RemoveAt(i);
            }

            if (_maskEntries.Count == 0 && _maskInstance != null)
                _maskInstance.SetActive(false);
        }

        /// <summary>句柄是否仍然有效。</summary>
        internal bool IsMaskHandleAlive(int token)
        {
            for (int i = 0; i < _maskEntries.Count; i++)
            {
                if (_maskEntries[i].Token == token)
                    return true;
            }

            return false;
        }

        /// <summary>释放一个句柄。幂等——重复释放不影响其它持有者。</summary>
        internal void ReleaseMask(int token)
        {
            for (int i = 0; i < _maskEntries.Count; i++)
            {
                if (_maskEntries[i].Token != token)
                    continue;

                _maskEntries.RemoveAt(i);
                break;
            }

            if (_maskEntries.Count == 0 && _maskInstance != null)
                _maskInstance.SetActive(false);
        }

        /// <summary>
        /// 创建遮罩实例（只做一次，之后复用）。不在此处挂 Button——点击开关由
        /// <see cref="ApplyMaskClickToClose"/> 单独管理，以便随时开关。
        /// </summary>
        private void EnsureMaskInstance()
        {
            if (_maskInstance != null)
                return;

            _maskInstance = new GameObject("UIManager_Mask", typeof(RectTransform));
            var rt = _maskInstance.GetComponent<RectTransform>();
            rt.SetParent(UIRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            var maskCanvas = _maskInstance.AddComponent<Canvas>();
            maskCanvas.overrideSorting = true;

            _maskInstance.AddComponent<UnityEngine.UI.Image>();
        }

        /// <summary>
        /// 应用样式。每次 ShowMask 都重设排序与颜色——多个系统各按自己的样式打开时，
        /// 以最后一次为准（遮罩是单实例，不按持有者分身份）。
        /// </summary>
        private void ApplyMaskStyle(UIMaskStyle style)
        {
            if (_maskInstance == null)
                return;

            var canvas = _maskInstance.GetComponent<Canvas>();
            if (canvas != null)
                canvas.sortingOrder = UISorting.MaskOrder(style.Layer);

            var image = _maskInstance.GetComponent<UnityEngine.UI.Image>();
            if (image != null)
                image.color = style.Color;
        }

        /// <summary>
        /// 应用点击关闭开关。创建路径与后续切换共用这一段，于是「事后改 clickToClose」也真的生效
        /// ——早先这个开关只在创建遮罩的那条分支里被读过。
        /// </summary>
        /// <remarks>
        /// 开关用 <c>Button.enabled</c> 而非增删组件：<c>Object.Destroy</c> 要到帧末才生效，
        /// 切换后同帧查询仍会看到残留组件；而反复增删也不符合本框架「少折腾对象」的取向。
        /// 被禁用的 <c>Selectable</c> 不再响应点击，但遮罩自身的 Image 照常挡射线，语义正确。
        /// </remarks>
        private void ApplyMaskClickToClose(bool clickToClose)
        {
            _maskClickToClose = clickToClose;

            if (_maskInstance == null)
                return;

            if (_maskButton == null)
            {
                if (!clickToClose)
                    return;

                _maskButton = _maskInstance.AddComponent<UnityEngine.UI.Button>();
                _maskButton.onClick.AddListener(OnMaskClicked);
            }

            _maskButton.enabled = clickToClose;
        }

        private void OnMaskClicked()
        {
            if (_maskClickToClose && CanGoBack)
            {
                PopAsync().Forget();
            }
        }

        #endregion

        #region Preload & Cache

        public async UniTask PreloadAsync<T>(string assetPath, CancellationToken cancellationToken = default)
            where T : UIPanelBase
        {
            EnsureInitialized();
            var type = typeof(T);

            if (_assetCache.ContainsKey(type))
                return;

            // 通过 AssetManager 预热对象池（加载资源但不实例化到场景中）
            await AssetManager.PreloadAllAsync(new[] { assetPath }, null, cancellationToken);
            _assetCache[type] = assetPath;
        }

        public void UnloadAsset<T>() where T : UIPanelBase
        {
            var type = typeof(T);
            _assetCache.Remove(type);
        }

        public void ClearAssetCache()
        {
            _assetCache.Clear();
        }

        #endregion

        #region Layer Management

        public void SetLayerVisibility(int layer, bool visible)
        {
            var container = GetLayerContainer(layer);
            if (container != null)
                container.gameObject.SetActive(visible);
        }

        public void SetLayerInteractive(int layer, bool interactive)
        {
            var container = GetLayerContainer(layer);
            if (container != null)
            {
                var childRaycasters = container.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>();
                foreach (var raycaster in childRaycasters)
                {
                    raycaster.enabled = interactive;
                }
            }
        }

        /// <summary>
        /// 获取指定层级的容器节点（可能已存在）。
        /// </summary>
        private Transform GetLayerContainer(int layer)
        {
            var containerName = $"Layer_{layer}";
            return UIRoot?.Find(containerName);
        }

        #endregion

        #region Sort Order

        public void BringToFront(UIPanelBase panel)
        {
            if (panel == null || panel.Canvas == null)
                return;

            var type = panel.GetType();
            if (!_activePanels.ContainsKey(type))
            {
                Debug.LogWarning($"[UIManager] BringToFront: Panel '{type.Name}' is not open.");
                return;
            }

            // 移到显示栈尾（= 最前），然后按栈位整体重排
            _stack.Remove(panel);
            _stack.Add(panel);
            RestampSortingOrders();
        }

        #endregion

        #region Per-Frame Update

        /// <inheritdoc cref="IUIManager.Update"/>
        public void Update(float deltaTime, float time)
        {
            // 手动驱动等价于驱动每帧档
            DriveLod(UpdateLOD.Tier0, deltaTime, time);
        }

        /// <summary>
        /// 驱动指定档位的面板。由每档对应的驱动器调用（Tier0 由门面的 FrameDriver 走 <see cref="Update"/>）。
        /// </summary>
        /// <param name="lod">本驱动器负责的档位。</param>
        /// <param name="deltaTime">距上次派发的间隔。</param>
        /// <param name="time">当前时刻。</param>
        internal void DriveLod(UpdateLOD lod, float deltaTime, float time)
        {
            if (!IsInitialized)
                return;

            PruneStack();

            int tier = Mathf.Clamp((int)lod, 0, _buckets.Length - 1);

            // 先在派发前重排：重排发生在遍历之外，于是面板在自己的 OnUpdate 里关掉自己
            // 或别的面板都不会让遍历错位——无需快照，也就没有每帧分配。
            // 代价是每次派发都 O(面板数)，但面板数通常 < 20，且不产生 GC。
            RebuildBuckets();

            var bucket = _buckets[tier];
            for (int i = 0; i < bucket.Count; i++)
            {
                var panel = bucket[i];

                // 失焦（IsPaused）的面板不派发：它被别的面板盖住了，看不见也没交互
                if (panel == null || !panel.IsOpen || panel.IsPaused)
                    continue;

                panel.OnUpdate(deltaTime, time);
            }

            // 注意：HUD 的 Update 已由 UIManager.Update 在 facade 层处理
        }

        /// <summary>
        /// 按各面板当前的 <see cref="UIViewBase.UpdateLod"/> 重建档位桶，并通知门面档位需求变化。
        /// </summary>
        private void RebuildBuckets()
        {
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i].Clear();

            for (int i = 0; i < _stack.Count; i++)
            {
                var panel = _stack[i];
                if (panel == null)
                    continue;

                int tier = Mathf.Clamp((int)panel.UpdateLod, 0, _buckets.Length - 1);
                _buckets[tier].Add(panel);
            }

            NotifyLodDemand();
        }

        /// <summary>
        /// 在档位「空 ↔ 非空」跳变时通知门面，使其懒注册/注销对应驱动器。
        /// 稳态下只做 8 次比较，不产生分配。
        /// </summary>
        private void NotifyLodDemand()
        {
            if (LodDemandChanged == null)
                return;

            for (int i = 0; i < _buckets.Length; i++)
            {
                bool active = _buckets[i].Count > 0;
                if (_lodDemand[i] == active)
                    continue;

                _lodDemand[i] = active;
                LodDemandChanged((UpdateLOD)i);
            }
        }

        /// <summary>
        /// 指定档位当前是否有面板。门面在收到档位需求变化后用它决定注册还是注销。
        /// </summary>
        internal bool HasPanelsAtLod(UpdateLOD lod)
        {
            int tier = Mathf.Clamp((int)lod, 0, _buckets.Length - 1);
            return _buckets[tier].Count > 0;
        }

        #endregion

        #region Internal — Panel Instantiation

        /// <summary>
        /// 实例化面板预制体，挂到该层级的容器下。
        /// </summary>
        private UniTask<T> InstantiatePanelAsync<T>(string assetPath, int layer,
            CancellationToken cancellationToken) where T : UIPanelBase
        {
            // 确定父节点（同一层级的 Container）
            var parent = GetOrCreateLayerContainer(layer);

            return _factory.CreateAsync<T>(assetPath, parent, cancellationToken);
        }

        /// <summary>
        /// 获取或创建指定层级的容器节点。
        /// <para>每个层级在 UIRoot 下有一个独立的子节点，自带 Canvas 实现层级间 Sorting Order 隔离。</para>
        /// </summary>
        private Transform GetOrCreateLayerContainer(int layer)
        {
            var containerName = $"Layer_{layer}";
            var existing = UIRoot.Find(containerName);
            if (existing != null)
                return existing;

            var go = new GameObject(containerName, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(UIRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            // 层级容器自带 Canvas，实现层级间渲染隔离
            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = UISorting.PanelOrder(layer, 0);
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            return go.transform;
        }

        #endregion

        #region Internal — In-flight Open

        /// <summary>
        /// 一次在途打开。仅被首个调用者持有；后来者把自己的完成源挂进 <see cref="Joiners"/>。
        /// </summary>
        private sealed class InFlightOpen
        {
            /// <summary>首个调用者请求的资源地址，仅用于诊断参数不一致的并发请求。</summary>
            public string AssetPath;

            /// <summary>
            /// 并发加入者各自的完成源。懒分配——绝大多数打开没有加入者。
            /// <para>加入者不能直接 await 打开者的 <c>UniTask</c>：UniTask 只支持单个
            /// continuation，第二个等待者会覆盖第一个。</para>
            /// </summary>
            public List<UniTaskCompletionSource<UIPanelBase>> Joiners;
        }

        /// <summary>
        /// 作为并发加入者等待一次在途打开完成。
        /// </summary>
        private async UniTask<T> JoinOpeningAsync<T>(InFlightOpen entry) where T : UIPanelBase
        {
            var tcs = new UniTaskCompletionSource<UIPanelBase>();

            if (entry.Joiners == null)
                entry.Joiners = new List<UniTaskCompletionSource<UIPanelBase>>(2);

            entry.Joiners.Add(tcs);

            var panel = await tcs.Task;
            return panel as T;
        }

        /// <summary>
        /// 结束一次在途打开：摘掉记录并把结果广播给全部加入者。
        /// </summary>
        /// <param name="opened">打开成功的面板；失败或未提交时为 null，加入者据此拿到 null。</param>
        private void SettleOpening(Type type, InFlightOpen entry, UIPanelBase opened)
        {
            _opening.Remove(type);

            if (entry.Joiners == null)
                return;

            for (int i = 0; i < entry.Joiners.Count; i++)
                entry.Joiners[i].TrySetResult(opened);

            entry.Joiners = null;
        }

        #endregion

        #region Internal — Panel Lifecycle

        /// <summary>
        /// 注册面板到活动字典，并入显示栈尾（新打开的面板显示在最前）。
        /// </summary>
        private void RegisterPanel(Type type, UIPanelBase panel)
        {
            // 有了在途打开去重，这里撞上旧实例说明「同类型单实例」不变量已被破坏，属实现缺陷。
            // 仍做兜底：把旧实例经正常回收路径处理，而不是静默产出一个 IsOpen 为 true 的池中失活引用。
            if (_activePanels.TryGetValue(type, out var oldPanel) && oldPanel != null)
            {
                Debug.LogError(
                    $"[UIManager] Panel '{type.Name}' is already active while registering a new instance. " +
                    "This breaks the one-instance-per-type invariant; the previous instance is being recycled.");
                RollbackPanel(type, oldPanel);
            }

            _activePanels[type] = panel;
            _stack.Add(panel);
            RestampSortingOrders();
        }

        /// <summary>
        /// 关闭面板的内部实现。面板回池由 UIManagerImpl 控制。
        /// </summary>
        /// <returns>确实完成了关闭返回 true；面板为空或已被 Controller 拦下返回 false。</returns>
        private async UniTask<bool> ClosePanelInternalAsync(UIPanelBase panel, Type type, bool immediate,
            CancellationToken cancellationToken)
        {
            if (panel == null)
                return false;

            cancellationToken.ThrowIfCancellationRequested();

            // 面板在自己的 OnOpen 里请求关闭时，延迟到 OnOpen 返回之后再关：
            // 在 OnOpen 的调用栈上重入 OnClose 与关闭动画，子类几乎必然写出
            // 「先初始化再被清理」的乱序。
            if (panel.IsOpening)
                await panel.WaitWhileOpeningAsync();

            // 等待期间令牌可能已被取消；此时尚未到达提交点，取消仍然有效
            cancellationToken.ThrowIfCancellationRequested();

            // ★ Controller 拦截点：关闭前校验
            var canClose = await _controller.OnBeforeCloseAsync(type, panel, immediate, cancellationToken);
            if (!canClose)
            {
                Debug.LogWarning($"[UIManager] Panel close blocked by Controller: {type.Name}");
                return false;
            }

            // 从字典和显示栈中移除
            _activePanels.Remove(type);
            _stack.Remove(panel);

            // 面板自己打开的遮罩随它一起释放，无需调用方记得配对关闭
            ReleaseMasksOwnedBy(panel);

            // 执行关闭逻辑（动画 + OnClose，不再自行 Destroy）
            await panel.DoCloseAsync(immediate);

            // 回池前通知面板，允许重置自定义状态
            panel.OnPoolRecycle();

            // 回池（默认实现下由 AssetManager 管理引用计数和池容量）
            _factory.Release(panel);
            MessageManager.Publish(new PanelClosedMessage(type));

            // ★ Controller 拦截点：关闭后回调
            await _controller.OnAfterCloseAsync(type, cancellationToken);
            return true;
        }

        /// <summary>
        /// 模糊栈顶面板（禁用交互）。
        /// </summary>
        /// <returns>被模糊的面板；栈为空时返回 null。调用方在后续步骤失败时应把它交回 <see cref="UnblurPanel"/>。</returns>
        private UIPanelBase BlurTopPanel()
        {
            PruneStack();

            if (_stack.Count == 0)
                return null;

            var top = _stack[_stack.Count - 1];
            top.OnBlur();
            return top;
        }

        /// <summary>
        /// 撤销一次 <see cref="BlurTopPanel"/>：只恢复交互，不动显示栈——栈根本没变，
        /// 回到焦点不等于回到栈顶。
        /// </summary>
        private void UnblurPanel(UIPanelBase panel)
        {
            if (panel != null && panel.IsOpen)
                panel.OnFocus();
        }

        /// <summary>
        /// 回滚一次失败的打开：从活动集合与显示栈中摘除，交还工厂。
        /// <para>幂等——未注册过时前两步是空操作，<c>OnPoolRecycle</c> 对尚未打开的面板也安全。</para>
        /// </summary>
        private void RollbackPanel(Type type, UIPanelBase panel)
        {
            _activePanels.Remove(type);
            _stack.Remove(panel);
            panel.OnPoolRecycle();
            _factory.Release(panel);
        }

        /// <summary>
        /// 恢复栈顶面板焦点（重新启用交互）。
        /// </summary>
        private void FocusTopPanel()
        {
            PruneStack();

            if (_stack.Count == 0)
                return;

            var top = _stack[_stack.Count - 1];
            BringToFront(top);
            top.OnFocus();
        }

        /// <summary>
        /// 清理栈中已被外部销毁（假空）的条目。
        /// <para>栈持有面板实例的强引用；第三方绕过 CloseAsync 直接 Destroy 面板时会留下空洞，
        /// 遍历前调一次，避免对已销毁对象派发回调。</para>
        /// </summary>
        private void PruneStack()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                // Unity 的 == 重载把已销毁对象判为 null
                if (_stack[i] == null)
                    _stack.RemoveAt(i);
            }
        }

        #endregion

        #region Internal — Sorting

        /// <summary>
        /// 按显示栈次序重排全部面板的 sortingOrder：层内序号 = 该面板之前同层面板的个数 + 1。
        /// <para>序号因此恒等于栈内相对次序，与显示栈是同一份真相——不再需要递增计数器，
        /// 也就没有计数器溢出到邻层区间的问题。</para>
        /// <para>O(n²) 但 n 通常 &lt; 20，且不分配；相比维护每层的游标表，这个写法没有需要复位的状态。</para>
        /// </summary>
        private void RestampSortingOrders()
        {
            for (int i = 0; i < _stack.Count; i++)
            {
                var panel = _stack[i];
                if (panel == null || panel.Canvas == null)
                    continue;

                int indexInLayer = 1;
                for (int j = 0; j < i; j++)
                {
                    var lower = _stack[j];
                    if (lower != null && lower.Layer == panel.Layer)
                        indexInLayer++;
                }

                if (indexInLayer > UISorting.MaxIndexInLayer)
                {
                    Debug.LogWarning(
                        $"[UIManager] Layer {panel.Layer} has more than {UISorting.MaxIndexInLayer} open panels; " +
                        "the extra ones are clamped to the top of the layer band and will share an order. " +
                        "Use a higher layer or close some of them (see UISorting).");
                }

                panel.Canvas.overrideSorting = true;
                panel.Canvas.sortingOrder = UISorting.PanelOrder(panel.Layer, indexInLayer);
            }
        }

        #endregion

        #region Internal — Message Handlers

        /// <summary>
        /// 接收 <see cref="LanguageChangedMessage"/>，自动通知所有已打开面板刷新文本。
        /// </summary>
        private void OnLanguageChangedMessage(LanguageChangedMessage msg)
        {
            if (!IsInitialized)
                return;

            PruneStack();

            // 与 Update 同理先快照：面板可能在自己的语言回调里关闭面板
            using (ListPool<UIPanelBase>.GetPooled(out var snapshot))
            {
                snapshot.AddRange(_stack);

                for (int i = 0; i < snapshot.Count; i++)
                {
                    var panel = snapshot[i];
                    if (panel != null && panel.IsOpen)
                    {
                        panel.OnLanguageChanged(msg.Language);
                    }
                }
            }
        }

        #endregion

        #region Internal — Validation

        private void EnsureInitialized()
        {
            if (!IsInitialized || UIRoot == null)
                throw new InvalidOperationException(
                    "[UIManager] UIManager 尚未初始化。请先调用 UIManager.Initialize(uiRoot) 完成初始化。");
        }

        #endregion
    }
}