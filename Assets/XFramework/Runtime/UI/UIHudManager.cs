using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// HUD 管理器默认实现。实现 <see cref="IUiHudProvider"/> 接口。
    /// <para>管理 <see cref="UIHudItem"/> 的附加、分离、对象池映射和每帧驱动。</para>
    /// <para>HUD 容器在 UIRoot 下自动创建（Layer_HUD），使用独立的 Canvas 与面板层级隔离。</para>
    /// <para>一个 3D 目标 Transform 同时只能绑定一个 HUD 实例，重复 Attach 会先 Detach 旧的。</para>
    /// <para>第三方可通过 <see cref="UIManager.SetHudProvider"/> 替换此实现。</para>
    /// </summary>
    internal sealed class UIHudManagerImpl : IUiHudProvider
    {
        #region Constants

        /// <summary>
        /// HUD 容器节点名称。
        /// </summary>
        private const string HudContainerName = "Layer_HUD";

        #endregion

        #region Fields

        /// <summary>
        /// HUD 容器节点（自动创建在 UIRoot 下）。
        /// </summary>
        private Transform _hudContainer;

        /// <summary>
        /// HUD 容器的 Canvas 组件（HUD 渲染用）。
        /// </summary>
        private Canvas _hudContainerCanvas;

        /// <summary>
        /// UIRoot 引用。
        /// </summary>
        private Transform _uiRoot;

        /// <summary>
        /// 映射表：3D 目标 Transform → UIHudItem 实例。
        /// <para>用于快速查找、去重和 Detach。</para>
        /// </summary>
        private readonly Dictionary<Transform, UIHudItem> _hudMap
            = new Dictionary<Transform, UIHudItem>(16);

        /// <summary>
        /// 当前所有活跃的 HUD 实例列表（用于每帧 OnUpdate 驱动）。
        /// </summary>
        private readonly List<UIHudItem> _activeHudList
            = new List<UIHudItem>(16);

        #endregion

        #region Properties

        /// <inheritdoc/>
        public bool HasActive => _activeHudList.Count > 0;

        #endregion

        #region IUiHudProvider

        /// <inheritdoc/>
        public void SetUIRoot(Transform uiRoot)
        {
            _uiRoot = uiRoot;
            // 更换 UIRoot 时重置容器引用
            _hudContainer = null;
            _hudContainerCanvas = null;
            // 清理所有已有 HUD（场景切换）
            DetachAll();
        }

        /// <inheritdoc/>
        public async UniTask<T> AttachAsync<T>(
            Transform target,
            string assetPath,
            Vector2? offset = null) where T : UIHudItem
        {
            if (target == null)
            {
                Debug.LogError("[UIHudManager] AttachAsync: target is null.");
                return null;
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("[UIHudManager] AttachAsync: assetPath is null or empty.");
                return null;
            }

            EnsureContainer();

            // 同一目标已有 HUD → 先 Detach
            if (_hudMap.TryGetValue(target, out var existing))
            {
                DetachInternal(existing, target);
            }

            // 实例化 HUD（AssetManager 内部管理对象池）
            var go = await AssetManager.InstantiateAsync(assetPath, _hudContainer);
            if (go == null)
            {
                Debug.LogError($"[UIHudManager] Failed to instantiate HUD: {typeof(T).Name} at path: {assetPath}");
                return null;
            }

            var hud = go.GetComponent<T>();
            if (hud == null)
            {
                Debug.LogError($"[UIHudManager] HUD prefab at '{assetPath}' lacks component '{typeof(T).Name}'. Destroying instance.");
                AssetManager.DestroyInstance(go);
                return null;
            }

            // 配置 HUD
            go.name = $"HUD_{typeof(T).Name}_{target.name}";
            hud.FollowTarget = target;
            hud.ScreenOffset = offset ?? Vector2.zero;
            hud.OnTargetLost += OnHudTargetLost;

            // 打开 HUD
            await hud.DoOpenAsync(null);

            // 注册映射
            _hudMap[target] = hud;
            _activeHudList.Add(hud);

            return hud;
        }

        /// <inheritdoc/>
        public void Detach(Transform target)
        {
            if (target == null)
                return;

            if (_hudMap.TryGetValue(target, out var hud))
            {
                DetachInternal(hud, target);
            }
        }

        /// <inheritdoc/>
        public void DetachAll()
        {
            // 收集所有条目（避免遍历中修改字典）
            var entries = new List<(UIHudItem hud, Transform target)>();
            foreach (var kv in _hudMap)
            {
                entries.Add((kv.Value, kv.Key));
            }

            foreach (var entry in entries)
            {
                DetachInternal(entry.hud, entry.target);
            }
        }

        /// <inheritdoc/>
        public void Update()
        {
            if (_activeHudList.Count == 0)
                return;

            // 倒序遍历，防止 HUD 回收时列表收缩导致索引错位
            for (int i = _activeHudList.Count - 1; i >= 0; i--)
            {
                var hud = _activeHudList[i];
                if (hud == null || !hud.IsOpen)
                {
                    _activeHudList.RemoveAt(i);
                    continue;
                }

                hud.OnUpdate();
            }
        }

        #endregion

        #region Private — Detach 内部实现

        /// <summary>
        /// Detach 内部实现：取消事件订阅 → 关闭 HUD → 回池 → 从映射和列表中移除。
        /// </summary>
        private void DetachInternal(UIHudItem hud, Transform target)
        {
            if (hud == null)
                return;

            // 取消事件订阅
            hud.OnTargetLost -= OnHudTargetLost;

            // 关闭 HUD（不等待动画完成，Forget）
            hud.DoCloseAsync(immediate: true).Forget();

            // 回池
            if (hud.gameObject != null)
            {
                AssetManager.DestroyInstance(hud.gameObject);
            }

            // 从映射和列表中移除
            if (target != null)
                _hudMap.Remove(target);

            _activeHudList.Remove(hud);
        }

        #endregion

        #region Private — Event Handlers

        /// <summary>
        /// HUD 目标丢失回调。自动 Detach 该 HUD。
        /// </summary>
        private void OnHudTargetLost(UIHudItem hud)
        {
            if (hud == null)
                return;

            var target = hud.FollowTarget;
            DetachInternal(hud, target);
        }

        #endregion

        #region Private — Container

        /// <summary>
        /// 确保 HUD 容器节点已创建。在 UIRoot 下创建 Layer_HUD 节点。
        /// </summary>
        private void EnsureContainer()
        {
            if (_hudContainer != null)
                return;

            if (_uiRoot == null)
            {
                Debug.LogError("[UIHudManager] EnsureContainer: uiRoot is null. HUD cannot be created.");
                return;
            }

            var existing = _uiRoot.Find(HudContainerName);
            if (existing != null)
            {
                _hudContainer = existing;
                _hudContainerCanvas = existing.GetComponent<Canvas>();
                return;
            }

            var go = new GameObject(HudContainerName, typeof(RectTransform));
            go.transform.SetParent(_uiRoot, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            // HUD 容器使用独立的 Canvas，放在 UI 最顶层
            _hudContainerCanvas = go.AddComponent<Canvas>();
            _hudContainerCanvas.overrideSorting = true;
            _hudContainerCanvas.sortingOrder = 999000; // 极高值，确保 HUD 始终渲染在最上层

            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            _hudContainer = go.transform;
        }

        #endregion
    }
}