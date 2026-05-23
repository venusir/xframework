using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// HUD 管理器。管理 <see cref="UIHudItem"/> 的附加、分离、对象池映射和每帧驱动。
    /// <para>通过 <see cref="UIManager.ShowHud{T}"/> / <see cref="UIManager.HideHud"/> 间接调用，不应直接使用此类。</para>
    /// <para>HUD 容器在 UIRoot 下自动创建（Layer_HUD），使用独立的 Canvas 与面板层级隔离。</para>
    /// <para>一个 3D 目标 Transform 同时只能绑定一个 HUD 实例，重复 Attach 会先 Detach 旧的。</para>
    /// </summary>
    internal static class UIHudManager
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
        private static Transform _hudContainer;

        /// <summary>
        /// HUD 容器的 Canvas 组件（HUD 渲染用）。
        /// </summary>
        private static Canvas _hudContainerCanvas;

        /// <summary>
        /// 映射表：3D 目标 Transform → UIHudItem 实例。
        /// <para>用于快速查找、去重和 Detach。</para>
        /// </summary>
        private static readonly Dictionary<Transform, UIHudItem> HudMap
            = new Dictionary<Transform, UIHudItem>(16);

        /// <summary>
        /// 当前所有活跃的 HUD 实例列表（用于每帧 OnUpdate 驱动）。
        /// </summary>
        private static readonly List<UIHudItem> ActiveHudList
            = new List<UIHudItem>(16);

        #endregion

        #region Internal API — Attach / Detach

        /// <summary>
        /// 为目标附加一个 HUD 实例。
        /// <para>如果该目标已有 HUD，先 Detach 旧的再 Attach 新的。</para>
        /// </summary>
        /// <typeparam name="T">HUD 类型（继承 <see cref="UIHudItem"/>）。</typeparam>
        /// <param name="target">要跟随的 3D 目标 Transform。</param>
        /// <param name="assetPath">HUD 预制体的 YooAsset 地址。</param>
        /// <param name="uiRoot">UIManager 的 UIRoot Transform。</param>
        /// <param name="offset">屏幕坐标偏移（像素）。</param>
        /// <returns>附加的 HUD 实例。如果 assetPath 为空则返回 null。</returns>
        internal static async UniTask<T> AttachAsync<T>(
            Transform target,
            string assetPath,
            Transform uiRoot,
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

            EnsureContainer(uiRoot);

            // 同一目标已有 HUD → 先 Detach
            if (HudMap.TryGetValue(target, out var existing))
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
            HudMap[target] = hud;
            ActiveHudList.Add(hud);

            return hud;
        }

        /// <summary>
        /// 分离指定目标绑定的 HUD。
        /// </summary>
        /// <param name="target">3D 目标 Transform。</param>
        internal static void Detach(Transform target)
        {
            if (target == null)
                return;

            if (HudMap.TryGetValue(target, out var hud))
            {
                DetachInternal(hud, target);
            }
        }

        /// <summary>
        /// 分离所有 HUD。
        /// </summary>
        internal static void DetachAll()
        {
            // 收集所有条目（避免遍历中修改字典）
            var entries = new List<(UIHudItem hud, Transform target)>();
            foreach (var kv in HudMap)
            {
                entries.Add((kv.Value, kv.Key));
            }

            foreach (var entry in entries)
            {
                DetachInternal(entry.hud, entry.target);
            }
        }

        #endregion

        #region Internal — Per-Frame Update

        /// <summary>
        /// 由 <see cref="UIManagerImpl.Update"/> 调用，驱动所有活动 HUD 的 <see cref="UIHudItem.OnUpdate"/>。
        /// <para>遍历过程中如果某个 HUD 因目标丢失被 Detach，使用倒序遍历避免索引错位。</para>
        /// </summary>
        internal static void Update()
        {
            if (ActiveHudList.Count == 0)
                return;

            // 倒序遍历，防止 HUD 回收时列表收缩导致索引错位
            for (int i = ActiveHudList.Count - 1; i >= 0; i--)
            {
                var hud = ActiveHudList[i];
                if (hud == null || !hud.IsOpen)
                {
                    ActiveHudList.RemoveAt(i);
                    continue;
                }

                hud.OnUpdate();
            }
        }

        #endregion

        #region Internal — HasActive

        /// <summary>
        /// 是否有活跃的 HUD。
        /// </summary>
        internal static bool HasActive => ActiveHudList.Count > 0;

        #endregion

        #region Private — Detach 内部实现

        /// <summary>
        /// Detach 内部实现：取消事件订阅 → 关闭 HUD → 回池 → 从映射和列表中移除。
        /// </summary>
        private static void DetachInternal(UIHudItem hud, Transform target)
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
                HudMap.Remove(target);

            ActiveHudList.Remove(hud);
        }

        #endregion

        #region Private — Event Handlers

        /// <summary>
        /// HUD 目标丢失回调。自动 Detach 该 HUD。
        /// </summary>
        private static void OnHudTargetLost(UIHudItem hud)
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
        private static void EnsureContainer(Transform uiRoot)
        {
            if (_hudContainer != null)
                return;

            if (uiRoot == null)
            {
                Debug.LogError("[UIHudManager] EnsureContainer: uiRoot is null. HUD cannot be created.");
                return;
            }

            var existing = uiRoot.Find(HudContainerName);
            if (existing != null)
            {
                _hudContainer = existing;
                _hudContainerCanvas = existing.GetComponent<Canvas>();
                return;
            }

            var go = new GameObject(HudContainerName, typeof(RectTransform));
            go.transform.SetParent(uiRoot, false);

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