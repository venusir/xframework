using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XLocalization;
using XFramework.XReactive;
using XFramework.XUI.Controller;
using XFramework.XUI.Data;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// <see cref="IUIManager"/> 的默认实现。
    /// <para>维护活动面板字典、导航堆栈、层级排序计数器和资源缓存。</para>
    /// <para>面板实例化使用 <see cref="AssetManager.InstantiateAsync(string, Transform)"/>，关闭时回池。</para>
    /// </summary>
    internal sealed class UIManagerImpl : IUIManager
    {
        #region Constants

        /// <summary>
        /// 每个层级的排序间隔。每打开一个面板，sorting order 增加此值。
        /// <para>例如层级 100 的面板从 100000 开始排序。</para>
        /// </summary>
        private const int SortOrderBase = 1000;

        /// <summary>
        /// 遮罩的默认层级（可外部配置）。
        /// </summary>
        private const int DefaultMaskLayer = 500;

        #endregion

        #region Fields

        /// <summary>
        /// 活动面板字典。key: 面板类型, value: 面板实例。
        /// </summary>
        private readonly Dictionary<Type, UIPanelBase> _activePanels
            = new Dictionary<Type, UIPanelBase>(8);

        /// <summary>
        /// 导航堆栈。顶部为当前显示的面板类型。
        /// </summary>
        private readonly List<Type> _navStack = new List<Type>(8);

        /// <summary>
        /// 每个层级的当前最高 sorting order。key: layer。
        /// </summary>
        private readonly Dictionary<int, int> _sortOrderCounters
            = new Dictionary<int, int>(4);

        /// <summary>
        /// 预加载资源路径缓存。key: 类型, value: assetPath。
        /// <para>标记哪些面板已被预加载到 AssetManager 的对象池中。</para>
        /// </summary>
        private readonly Dictionary<Type, string> _assetCache
            = new Dictionary<Type, string>(8);

        /// <summary>
        /// 遮罩 GameObject 实例。
        /// </summary>
        private GameObject _maskInstance;

        /// <summary>
        /// 遮罩是否启用了点击关闭功能。
        /// </summary>
        private bool _maskClickToClose;

        /// <summary>
        /// UI 控制器。用于拦截面板打开/关闭流程。
        /// <para>默认使用 <see cref="UIDefaultController"/>，可通过 <see cref="SetController"/> 替换。</para>
        /// </summary>
        private IUIController _controller;

        /// <summary>
        /// 语言变更消息订阅句柄。Dispose 时取消订阅。
        /// </summary>
        private IDisposable _languageChangedSubscription;

        #endregion

        #region Properties

        public bool IsInitialized { get; private set; }

        public Transform UIRoot { get; private set; }

        public bool IsMaskShowing => _maskInstance != null && _maskInstance.activeSelf;

        public bool HasPrevious => _navStack.Count > 1;

        #endregion

        #region Initialization

        /// <summary>
        /// 初始化 UI 管理器，设置 UI 根节点。
        /// </summary>
        /// <param name="uiRoot">场景中 UIRootNode 的 Transform。</param>
        internal void Initialize(Transform uiRoot)
        {
            if (uiRoot == null)
                throw new ArgumentNullException(nameof(uiRoot));

            UIRoot = uiRoot;
            IsInitialized = true;

            // 默认使用 UIDefaultController（所有操作直接放行）
            _controller = new UIDefaultController();

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
                if (panel != null && panel.gameObject != null)
                {
                    AssetManager.DestroyInstance(panel.gameObject);
                }
            }

            _activePanels.Clear();
            _navStack.Clear();
            _sortOrderCounters.Clear();

            // 清理缓存（AssetManager 对象池由 AssetManager.Dispose 统一管理）
            _assetCache.Clear();

            // 隐藏遮罩
            if (_maskInstance != null)
            {
                AssetManager.DestroyInstance(_maskInstance);
                _maskInstance = null;
            }

            UIRoot = null;
            IsInitialized = false;
        }

        #endregion

        #region Basic Panel Management

        public async UniTask<T> OpenAsync<T>(string assetPath, int layer = 100, object userData = null)
            where T : UIPanelBase
        {
            EnsureInitialized();
            var type = typeof(T);

            // 已打开的面板直接聚焦（不触发 Controller 拦截）
            if (_activePanels.TryGetValue(type, out var existingPanel))
            {
                BringToFront(existingPanel);
                return existingPanel as T;
            }

            // ★ Controller 拦截点：打开前校验
            var canOpen = await _controller.OnBeforeOpenAsync(type, assetPath, layer, userData);
            if (!canOpen)
            {
                Debug.LogWarning($"[UIManager] Panel open blocked by Controller: {type.Name}");
                return null;
            }

            // 实例化面板（AssetManager 管理对象池）
            var panel = await InstantiatePanelAsync<T>(assetPath, layer);

            if (panel == null)
            {
                Debug.LogError($"[UIManager] Failed to instantiate panel: {type.Name} at path: {assetPath}");
                return null;
            }

            panel.Layer = layer;
            panel.AssetPath = assetPath;

            // 设置 sorting order
            var order = GetNextSortingOrder(layer);
            panel.Canvas.overrideSorting = true;
            panel.Canvas.sortingOrder = order;

            // 注册并打开
            RegisterPanel(type, panel);
            await panel.DoOpenAsync(userData);
            MessageManager.Publish(new PanelOpenedMessage(type));

            // ★ Controller 拦截点：打开后回调
            await _controller.OnAfterOpenAsync(type, panel, userData);

            return panel;
        }

        public async UniTask CloseAsync<T>(bool immediate = false) where T : UIPanelBase
        {
            EnsureInitialized();
            var type = typeof(T);

            if (_activePanels.TryGetValue(type, out var panel))
            {
                await ClosePanelInternalAsync(panel, type, immediate);
            }
        }

        public async UniTask CloseAsync(UIPanelBase panel, bool immediate = false)
        {
            EnsureInitialized();
            if (panel == null)
                return;

            var type = panel.GetType();
            if (_activePanels.ContainsKey(type))
            {
                await ClosePanelInternalAsync(panel, type, immediate);
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

        public async UniTask CloseLayerAsync(int layer, bool immediate = false)
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
                await ClosePanelInternalAsync(item.panel, item.type, immediate);
            }
        }

        public async UniTask CloseAllAsync(bool immediate = false)
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
                await ClosePanelInternalAsync(item.panel, item.type, immediate);
            }

            MessageManager.Publish(new AllPanelsClosedMessage());

            // ★ Controller 拦截点：全部关闭后回调
            await _controller.OnAllPanelsClosedAsync();
        }

        #endregion

        #region Stack Navigation

        public async UniTask<T> PushAsync<T>(string assetPath, int layer = 100, object userData = null)
            where T : UIPanelBase
        {
            EnsureInitialized();
            var type = typeof(T);

            // 如果面板已打开，先聚焦它
            if (_activePanels.TryGetValue(type, out var existingPanel))
            {
                // 从堆栈中移除旧的然后再推入顶层
                _navStack.Remove(type);
                _navStack.Add(type);
                BringToFront(existingPanel);
                return existingPanel as T;
            }

            // 模糊当前栈顶面板
            BlurTopPanel();

            // 打开新面板
            var panel = await OpenAsync<T>(assetPath, layer, userData);

            if (panel != null)
            {
                _navStack.Add(type);
            }

            return panel;
        }

        public async UniTask PopAsync(bool immediate = false)
        {
            EnsureInitialized();
            if (_navStack.Count <= 1)
                return;

            // 移除栈顶
            var topType = _navStack[_navStack.Count - 1];
            _navStack.RemoveAt(_navStack.Count - 1);

            // 关闭栈顶面板
            if (_activePanels.TryGetValue(topType, out var topPanel))
            {
                await ClosePanelInternalAsync(topPanel, topType, immediate);
            }

            // 恢复上一个面板焦点
            FocusTopPanel();
        }

        public async UniTask BackToAsync<T>(bool immediate = false) where T : UIPanelBase
        {
            EnsureInitialized();
            var targetType = typeof(T);
            var targetIndex = _navStack.IndexOf(targetType);

            if (targetIndex < 0)
            {
                Debug.LogWarning($"[UIManager] BackToAsync: Panel '{targetType.Name}' not found in navigation stack.");
                return;
            }

            // 从栈顶到目标之后的面板依次关闭
            for (int i = _navStack.Count - 1; i > targetIndex; i--)
            {
                var type = _navStack[i];
                if (_activePanels.TryGetValue(type, out var panel))
                {
                    await ClosePanelInternalAsync(panel, type, immediate);
                }
                _navStack.RemoveAt(i);
            }

            // 恢复目标面板焦点
            FocusTopPanel();
        }

        public async UniTask GoBackAsync(bool immediate = false)
        {
            await PopAsync(immediate);
        }

        #endregion

        #region Modal Mask

        public void ShowMask(int maskLayer = DefaultMaskLayer, float alpha = 0.5f, bool clickToClose = false)
        {
            EnsureInitialized();

            if (_maskInstance != null)
            {
                _maskInstance.SetActive(true);
            }
            else
            {
                // 创建一个简单的全屏遮罩
                _maskInstance = new GameObject("UIManager_Mask", typeof(RectTransform));
                var rt = _maskInstance.GetComponent<RectTransform>();
                rt.SetParent(UIRoot, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;

                // Canvas 用于设置排序层级
                var maskCanvas = _maskInstance.AddComponent<Canvas>();
                maskCanvas.overrideSorting = true;
                maskCanvas.sortingOrder = maskLayer * SortOrderBase;

                // Image 用于渲染颜色
                var maskImage = _maskInstance.AddComponent<UnityEngine.UI.Image>();
                maskImage.color = new Color(0, 0, 0, alpha);

                // 如果需要点击关闭，添加 Button
                if (clickToClose)
                {
                    var button = _maskInstance.AddComponent<UnityEngine.UI.Button>();
                    button.onClick.AddListener(OnMaskClicked);
                    _maskClickToClose = true;
                }
            }

            // 更新排序
            var canvas = _maskInstance.GetComponent<Canvas>();
            if (canvas != null)
                canvas.sortingOrder = maskLayer * SortOrderBase;

            // 更新透明度
            var img = _maskInstance.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
                img.color = new Color(0, 0, 0, alpha);
        }

        public void HideMask()
        {
            if (_maskInstance != null)
            {
                _maskInstance.SetActive(false);
            }
        }

        private void OnMaskClicked()
        {
            if (_maskClickToClose && _navStack.Count > 1)
            {
                PopAsync().Forget();
            }
        }

        #endregion

        #region Preload & Cache

        public async UniTask PreloadAsync<T>(string assetPath) where T : UIPanelBase
        {
            EnsureInitialized();
            var type = typeof(T);

            if (_assetCache.ContainsKey(type))
                return;

            // 通过 AssetManager 预热对象池（加载资源但不实例化到场景中）
            await AssetManager.PreloadAllAsync(new[] { assetPath });
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

        public int GetTopSortingOrder(int layer)
        {
            _sortOrderCounters.TryGetValue(layer, out var counter);
            return counter * SortOrderBase + SortOrderBase;
        }

        public void BringToFront(UIPanelBase panel)
        {
            if (panel == null || panel.Canvas == null)
                return;

            var order = GetNextSortingOrder(panel.Layer);
            panel.Canvas.sortingOrder = order;

            // 如果是堆栈中的面板，Update nav stack order
            var type = panel.GetType();
            if (_navStack.Contains(type))
            {
                _navStack.Remove(type);
                _navStack.Add(type);
            }
        }

        #endregion

        #region Per-Frame Update

        /// <inheritdoc cref="IUIManager.Update"/>
        public void Update()
        {
            if (!IsInitialized)
                return;

            // 遍历所有活动面板，仅驱动 IsOpen 的（OnBlur 的面板不执行 OnUpdate）
            foreach (var kv in _activePanels)
            {
                var panel = kv.Value;
                if (panel != null && panel.IsOpen)
                {
                    panel.OnUpdate();
                }
            }

            // 注意：HUD 的 Update 已由 UIManager.Update 在 facade 层处理
        }

        #endregion

        #region Internal — Panel Instantiation

        /// <summary>
        /// 实例化面板预制体。直接通过 AssetManager 加载/复用对象池实例。
        /// </summary>
        private async UniTask<T> InstantiatePanelAsync<T>(string assetPath, int layer) where T : UIPanelBase
        {
            var type = typeof(T);

            // 确定父节点（同一层级的 Container）
            var parent = GetOrCreateLayerContainer(layer);

            // AssetManager.InstantiateAsync 内部已处理对象池逻辑：
            // 池中有闲置实例 → 直接复用，池中无 → 加载资源并实例化
            var go = await AssetManager.InstantiateAsync(assetPath, parent);
            if (go == null)
                return null;

            go.name = type.Name;

            var panel = go.GetComponent<T>();
            if (panel == null)
            {
                Debug.LogError($"[UIManager] Prefab at '{assetPath}' lacks component {type.Name}. Destroying instance.");
                AssetManager.DestroyInstance(go);
                return null;
            }

            return panel;
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
            canvas.sortingOrder = layer * SortOrderBase;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            return go.transform;
        }

        #endregion

        #region Internal — Panel Lifecycle

        /// <summary>
        /// 注册面板到活动字典。
        /// </summary>
        private void RegisterPanel(Type type, UIPanelBase panel)
        {
            // 如果同类型已存在，先移除旧的（回池而非 Destroy）
            if (_activePanels.TryGetValue(type, out var oldPanel) && oldPanel != null)
            {
                if (oldPanel.gameObject != null)
                    AssetManager.DestroyInstance(oldPanel.gameObject);
            }

            _activePanels[type] = panel;
        }

        /// <summary>
        /// 关闭面板的内部实现。面板回池由 UIManagerImpl 控制。
        /// </summary>
        private async UniTask ClosePanelInternalAsync(UIPanelBase panel, Type type, bool immediate)
        {
            if (panel == null)
                return;

            // ★ Controller 拦截点：关闭前校验
            var canClose = await _controller.OnBeforeCloseAsync(type, panel, immediate);
            if (!canClose)
            {
                Debug.LogWarning($"[UIManager] Panel close blocked by Controller: {type.Name}");
                return;
            }

            // 从字典和堆栈中移除
            _activePanels.Remove(type);
            _navStack.Remove(type);

            // 执行关闭逻辑（动画 + OnClose，不再自行 Destroy）
            await panel.DoCloseAsync(immediate);

            // 回池前通知面板，允许重置自定义状态
            panel.OnPoolRecycle();

            // 回池（AssetManager 内部管理引用计数和池容量）
            AssetManager.DestroyInstance(panel.gameObject);
            MessageManager.Publish(new PanelClosedMessage(type));

            // ★ Controller 拦截点：关闭后回调
            await _controller.OnAfterCloseAsync(type);
        }

        /// <summary>
        /// 模糊栈顶面板（禁用交互）。
        /// </summary>
        private void BlurTopPanel()
        {
            if (_navStack.Count > 0)
            {
                var topType = _navStack[_navStack.Count - 1];
                if (_activePanels.TryGetValue(topType, out var topPanel) && topPanel != null)
                {
                    topPanel.OnBlur();
                }
            }
        }

        /// <summary>
        /// 恢复栈顶面板焦点（重新启用交互）。
        /// </summary>
        private void FocusTopPanel()
        {
            if (_navStack.Count > 0)
            {
                var topType = _navStack[_navStack.Count - 1];
                if (_activePanels.TryGetValue(topType, out var topPanel) && topPanel != null)
                {
                    BringToFront(topPanel);
                    topPanel.OnFocus();
                }
            }
        }

        #endregion

        #region Internal — Sorting

        /// <summary>
        /// 获取指定层级的下一个 sorting order 并递增计数器。
        /// </summary>
        private int GetNextSortingOrder(int layer)
        {
            _sortOrderCounters.TryGetValue(layer, out var counter);
            counter++;
            _sortOrderCounters[layer] = counter;
            return layer * SortOrderBase + counter;
        }

        #endregion

        #region Internal — Message Handlers

        /// <summary>
        /// 接收 <see cref="LanguageChangedMessage"/>，自动通知所有已打开面板刷新文本。
        /// </summary>
        private void OnLanguageChangedMessage(LanguageChangedMessage msg)
        {
            foreach (var kv in _activePanels)
            {
                if (kv.Value != null && kv.Value.IsOpen)
                {
                    kv.Value.OnLanguageChanged(msg.Language);
                }
            }
        }

        #endregion

        #region Internal — Validation

        private void EnsureInitialized()
        {
            if (!IsInitialized || UIRoot == null)
                throw new InvalidOperationException(
                    "UIManager is not initialized. Call UIManager.Initialize(uiRoot) first.");
        }

        #endregion
    }
}