using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;

namespace XFramework.XUI
{
    /// <summary>
    /// <see cref="IUIManager"/> 的默认实现。
    /// <para>维护活动面板字典、导航堆栈、层级排序计数器和资源缓存。</para>
    /// <para>面板实例化使用 <see cref="AssetManager.InstantiateAsync{T}(string, Transform)"/>。</para>
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
        /// 预加载资源缓存。key: 类型, value: (assetPath, GameObject)。
        /// <para>预制体被预加载后缓存于此，OpenAsync 时直接实例化。</para>
        /// </summary>
        private readonly Dictionary<Type, (string path, GameObject prefab)> _assetCache
            = new Dictionary<Type, (string, GameObject)>(8);

        /// <summary>
        /// 遮罩 GameObject 实例。
        /// </summary>
        private GameObject _maskInstance;

        /// <summary>
        /// 遮罩是否启用了点击关闭功能。
        /// </summary>
        private bool _maskClickToClose;

        #endregion

        #region Properties

        public bool IsInitialized { get; private set; }

        public Transform UIRoot { get; private set; }

        public bool IsMaskShowing => _maskInstance != null && _maskInstance.activeSelf;

        public bool HasPrevious => _navStack.Count > 1;

        #endregion

        #region Events

        public event Action<Type> OnPanelOpened;
        public event Action<Type> OnPanelClosed;
        public event Action OnAllPanelsClosed;

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
        }

        /// <summary>
        /// 销毁所有内容。
        /// </summary>
        public void Dispose()
        {
            // 同步关闭所有面板
            var panels = new List<UIPanelBase>(_activePanels.Values);
            foreach (var panel in panels)
            {
                if (panel != null && panel.gameObject != null)
                {
                    UnityEngine.Object.Destroy(panel.gameObject);
                }
            }

            _activePanels.Clear();
            _navStack.Clear();
            _sortOrderCounters.Clear();

            // 清理缓存
            foreach (var kv in _assetCache)
            {
                if (kv.Value.prefab != null)
                    AssetManager.DestroyInstance(kv.Value.prefab);
            }
            _assetCache.Clear();

            // 隐藏遮罩
            if (_maskInstance != null)
            {
                UnityEngine.Object.Destroy(_maskInstance);
                _maskInstance = null;
            }

            UIRoot = null;
            IsInitialized = false;
            OnPanelOpened = null;
            OnPanelClosed = null;
            OnAllPanelsClosed = null;
        }

        #endregion

        #region Basic Panel Management

        public async UniTask<T> OpenAsync<T>(string assetPath, int layer = 100, object userData = null)
            where T : UIPanelBase
        {
            EnsureInitialized();
            var type = typeof(T);

            // 已打开的面板直接聚焦
            if (_activePanels.TryGetValue(type, out var existingPanel))
            {
                BringToFront(existingPanel);
                return existingPanel as T;
            }

            // 实例化面板
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
            OnPanelOpened?.Invoke(type);

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

            // 收集所有面板
            var toClose = new List<(UIPanelBase panel, Type type)>();
            foreach (var kv in _activePanels)
            {
                toClose.Add((kv.Value, kv.Key));
            }

            foreach (var item in toClose)
            {
                await ClosePanelInternalAsync(item.panel, item.type, immediate);
            }

            OnAllPanelsClosed?.Invoke();
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

            var prefab = await LoadPrefabAsync(assetPath);
            if (prefab != null)
            {
                // 不激活，只缓存预制体引用
                prefab.SetActive(false);
                _assetCache[type] = (assetPath, prefab);
            }
        }

        public void UnloadAsset<T>() where T : UIPanelBase
        {
            var type = typeof(T);
            if (_assetCache.TryGetValue(type, out var entry))
            {
                if (entry.prefab != null)
                    AssetManager.DestroyInstance(entry.prefab);
                _assetCache.Remove(type);
            }
        }

        public void ClearAssetCache()
        {
            foreach (var kv in _assetCache)
            {
                if (kv.Value.prefab != null)
                    AssetManager.DestroyInstance(kv.Value.prefab);
            }
            _assetCache.Clear();
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

        #region Localization Event

        /// <summary>
        /// 语言切换时，通知所有已打开面板。
        /// </summary>
        public void OnLanguageChanged(string lang)
        {
            foreach (var kv in _activePanels)
            {
                if (kv.Value != null && kv.Value.IsOpen)
                {
                    kv.Value.OnLanguageChanged(lang);
                }
            }
        }

        #endregion

        #region Internal — Panel Instantiation

        /// <summary>
        /// 实例化面板预制体。优先从缓存获取，否则通过 AssetManager 加载。
        /// </summary>
        private async UniTask<T> InstantiatePanelAsync<T>(string assetPath, int layer) where T : UIPanelBase
        {
            var type = typeof(T);

            // 从缓存中获取预制体
            GameObject prefab = null;
            if (_assetCache.TryGetValue(type, out var cacheEntry))
            {
                prefab = cacheEntry.prefab;
            }

            // 未缓存，通过 AssetManager 加载
            if (prefab == null)
            {
                prefab = await LoadPrefabAsync(assetPath);
            }

            if (prefab == null)
                return null;

            // 确定父节点（同一层级的 Container）
            var parent = GetOrCreateLayerContainer(layer);
            var go = UnityEngine.Object.Instantiate(prefab, parent, false);
            go.name = type.Name;

            return go.GetComponent<T>();
        }

        /// <summary>
        /// 异步加载预制体。
        /// </summary>
        private async UniTask<GameObject> LoadPrefabAsync(string assetPath)
        {
            try
            {
                var go = await AssetManager.InstantiateAsync(assetPath, null);
                go.SetActive(false);
                ObjectVisibilityHelper.DontDestroyOnLoad(go);
                return go;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIManager] Failed to load panel prefab at path: {assetPath}. Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取或创建指定层级的容器节点。
        /// <para>每个层级在 UIRoot 下有一个独立的子节点，包含 Canvas，用于控制该层级内面板间的排序。</para>
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

            return go.transform;
        }

        #endregion

        #region Internal — Panel Lifecycle

        /// <summary>
        /// 注册面板到活动字典。
        /// </summary>
        private void RegisterPanel(Type type, UIPanelBase panel)
        {
            // 如果同类型已存在，先移除旧的
            if (_activePanels.TryGetValue(type, out var oldPanel) && oldPanel != null)
            {
                if (oldPanel.gameObject != null)
                    UnityEngine.Object.Destroy(oldPanel.gameObject);
            }

            _activePanels[type] = panel;
        }

        /// <summary>
        /// 关闭面板的内部实现。
        /// </summary>
        private async UniTask ClosePanelInternalAsync(UIPanelBase panel, Type type, bool immediate)
        {
            if (panel == null)
                return;

            // 从字典和堆栈中移除
            _activePanels.Remove(type);
            _navStack.Remove(type);

            // 执行关闭逻辑
            await panel.DoCloseAsync(immediate);
            OnPanelClosed?.Invoke(type);
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

        #region Internal — Validation

        private void EnsureInitialized()
        {
            if (!IsInitialized || UIRoot == null)
                throw new InvalidOperationException(
                    "UIManager is not initialized. Call UIManager.Initialize(uiRoot) first.");
        }

        #endregion
    }

    /// <summary>
    /// 内部辅助类，用于控制实例化对象的 DontDestroyOnLoad 行为。
    /// </summary>
    internal static class ObjectVisibilityHelper
    {
        /// <summary>
        /// 设置对象在场景加载时不销毁。
        /// <para>DontDestroyOnLoad 要求根对象激活，此方法自动处理未激活对象的激活/回复状态。</para>
        /// </summary>
        public static void DontDestroyOnLoad(GameObject go)
        {
            if (go == null)
                return;

            var wasActive = go.activeSelf;
            if (!wasActive)
                go.SetActive(true);
            UnityEngine.Object.DontDestroyOnLoad(go);
            if (!wasActive)
                go.SetActive(false);
        }
    }
}