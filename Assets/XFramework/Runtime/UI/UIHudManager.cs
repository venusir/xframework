using System;
using System.Collections.Generic;
using System.Threading;
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

        /// <summary>
        /// 「清空」的世代号，每次 <see cref="DetachAll"/> 自增。在途的 <see cref="AttachAsync"/>
        /// 完成后据此判断自己是不是上一世代的产物——是则立刻回池，不进入映射与驱动列表。
        /// <para><b>为什么不是布尔标志</b>：<see cref="DetachAll"/> 有两个性质不同的调用方——
        /// 「管理器要退役了」（<c>Dispose</c>、换根）与「只是把在播的清掉」（<c>CloseAllAsync</c>）。
        /// 粘性布尔会把后者也当成退役，此后每次 Attach 都会实例化完立刻回收。世代号只回答
        /// 「你有没有被哪一次清空落下」，与「管理器是否还在服役」解耦。（Tip 侧踩过同一个坑。）</para>
        /// </summary>
        private int _detachGeneration;

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
            Vector2? offset = null,
            CancellationToken cancellationToken = default) where T : UIHudItem
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

            // 记下进入时的世代：实例化与打开期间若发生过 DetachAll，回来的实例就属于上一世代
            int generation = _detachGeneration;

            // 实例化 HUD（AssetManager 内部管理对象池）
            var go = await AssetManager.InstantiateAsync(assetPath, _hudContainer, cancellationToken);
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

            try
            {
                // 打开 HUD
                await hud.DoOpenAsync(null);
            }
            catch
            {
                // 打开失败时实例还没进任何映射（DetachAll 找不到它），必须自己收干净：解订阅、清目标、
                // 回池。否则它就是一个常驻 Layer_HUD、还持着资源引用的孤儿。面板侧有回滚路径兜住，
                // HUD 侧此前没有。
                DiscardHud(hud);
                throw;
            }

            // 等待期间发生过清空（Destroy / 换根 / CloseAllAsync）：立刻收干净，不进入映射与驱动列表
            if (generation != _detachGeneration)
            {
                DiscardHud(hud);
                return null;
            }

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
            // 推进世代：在途 Attach 回来后据此回池。这不是「管理器退役」的标记——退役与否由调用方
            // 决定（Dispose 之后没人会再调 AttachAsync），故此处置位不影响后续正常使用。
            _detachGeneration++;

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
        public void Update(float deltaTime, float time)
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

                hud.OnUpdate(deltaTime, time);
            }
        }

        #endregion

        #region Private — Detach 内部实现

        /// <summary>
        /// Detach 内部实现：取消事件订阅 → 摘账 → 关闭并回池。
        /// </summary>
        private void DetachInternal(UIHudItem hud, Transform target)
        {
            if (hud == null)
                return;

            // 取消事件订阅
            hud.OnTargetLost -= OnHudTargetLost;

            // 先摘账（同步完成），再异步关闭并回收。摘账按实例反查而不是按 target 删——target 可能
            // 为 null（HUD 自己上报目标丢失时 FollowTarget 已被清），那条以旧 target 为键的条目会留在
            // 映射里指向一个已回池的实例，下次 DetachAll 遍历到它就会二次回收。
            RemoveMapEntry(hud);
            _activeHudList.Remove(hud);

            // 关闭与回收必须串行：Detach 是同步 API 不能 await，故把回收放进续体。
            // 反过来（先回收再等续体）会让 DoCloseAsync 的收尾落在已经回池、甚至已被另一个目标复用的
            // 实例上——把新持有者的 HUD 关掉并失活。
            CloseAndRecycleAsync(hud).Forget();
        }

        /// <summary>
        /// 按实例反查并移除映射条目。
        /// <para>用 <see cref="ReferenceEquals"/> 比较：这里的相等语义是「同一个实例」，
        /// 不该走 Unity 那套「已销毁即等于 null」的重载。</para>
        /// </summary>
        private void RemoveMapEntry(UIHudItem hud)
        {
            bool found = false;
            Transform stale = null;

            foreach (var kv in _hudMap)
            {
                if (ReferenceEquals(kv.Value, hud))
                {
                    stale = kv.Key;
                    found = true;
                    break;
                }
            }

            if (found)
                _hudMap.Remove(stale);
        }

        /// <summary>
        /// 关闭 HUD 并回池。由 <see cref="DetachInternal"/> 的续体调用——Detach 是同步 API，
        /// 而关闭可能带异步动画，两者必须串行。
        /// </summary>
        private static async UniTask CloseAndRecycleAsync(UIHudItem hud)
        {
            try
            {
                await hud.DoCloseAsync(immediate: true);
            }
            catch (Exception e)
            {
                // 关闭失败也必须往下走：实例已从映射与列表摘除，没有第二条路径能再碰到它
                Debug.LogError($"[UIHudManager] HUD close failed, recycling anyway: {e}");
            }

            RecycleHud(hud);
        }

        /// <summary>
        /// 把一个尚未进入映射的 HUD 收干净：解订阅、清目标、回池。打开失败与「等待期间被清空」共用。
        /// </summary>
        private void DiscardHud(UIHudItem hud)
        {
            if (hud == null)
                return;

            hud.OnTargetLost -= OnHudTargetLost;
            hud.FollowTarget = null;
            RecycleHud(hud);
        }

        /// <summary>
        /// 回池：先复位（释放 Track 登记的订阅、清 Canvas 排序——HUD 同样是回池而非销毁，
        /// 漏掉复位会让排序值留在实例上，复用时可能盖住不该盖的面板），再交还资源层。
        /// </summary>
        private static void RecycleHud(UIHudItem hud)
        {
            if (hud == null)
                return;

            hud.OnPoolRecycle();

            if (hud.gameObject != null)
            {
                AssetManager.DestroyInstance(hud.gameObject);
            }
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
            _hudContainerCanvas.sortingOrder = UISorting.HudOrder;

            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            _hudContainer = go.transform;
        }

        #endregion
    }
}