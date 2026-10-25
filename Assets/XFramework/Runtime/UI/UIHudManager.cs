using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XLog;
using XFramework.XUI.View;

namespace XFramework.XUI
{
    /// <summary>
    /// HUD 管理器默认实现。实现 <see cref="IUIHudProvider"/> 接口。
    /// <para>管理 <see cref="UIHudItem"/> 的附加、分离、对象池映射和每帧驱动。</para>
    /// <para>HUD 容器在 UIRoot 下自动创建（Layer_HUD），使用独立的 Canvas 与面板层级隔离。</para>
    /// <para>一个 3D 目标 Transform 同时只能绑定一个 HUD 实例，重复 Attach 会先 Detach 旧的。</para>
    /// <para>第三方可通过 <see cref="UIManager.SetHudProvider"/> 替换此实现。</para>
    /// </summary>
    internal sealed class UIHudManagerImpl : IUIHudProvider
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

        /// <summary>
        /// 按 target 的单槽在途表（R4-UIB-002）。完全相同请求的后来者加入同一次 Attach；
        /// 同 target 的不同 HUD 请求等当前在途结束后重试，避免并发实例化后先后写 map 产生孤儿。
        /// <para>键用实例 ID，避免已销毁 Transform 的 Unity 伪 null 干扰字典哈希。</para>
        /// </summary>
        private readonly Dictionary<int, InFlightAttach> _inFlightAttach
            = new Dictionary<int, InFlightAttach>(8);

        private sealed class InFlightAttach
        {
            public int TargetId;
            public Transform Target;
            public Type HudType;
            public string AssetPath;
            public List<UniTaskCompletionSource<UIHudItem>> Joiners;
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public bool HasActive => _activeHudList.Count > 0;

        #endregion

        #region IUIHudProvider

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
                LogManager.Error(LogCategories.UIHudManager, "AttachAsync: target is null.");
                return null;
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                LogManager.Error(LogCategories.UIHudManager, "AttachAsync: assetPath is null or empty.");
                return null;
            }

            if (!EnsureContainer())
                return null;

            int targetId = target.GetInstanceID();

            // 同 target 的单槽在途：完全相同请求加入；不同请求等当前在途结束后重试。
            while (true)
            {
                if (_inFlightAttach.TryGetValue(targetId, out var inFlight))
                {
                    if (inFlight.HudType == typeof(T) &&
                        string.Equals(inFlight.AssetPath, assetPath, StringComparison.Ordinal))
                    {
                        var joined = await JoinAttachAsync(inFlight, cancellationToken);
                        return joined as T;
                    }

                    await WaitAttachAsync(inFlight, cancellationToken);
                    continue;
                }

                break;
            }

            // 同一目标已有 HUD → 先 Detach
            if (_hudMap.TryGetValue(target, out var existing))
            {
                DetachInternal(existing, target);
            }

            // 记下进入时的世代：实例化与打开期间若发生过 DetachAll，回来的实例就属于上一世代
            int generation = _detachGeneration;

            var entry = new InFlightAttach
            {
                TargetId = targetId,
                Target = target,
                HudType = typeof(T),
                AssetPath = assetPath,
            };
            _inFlightAttach[targetId] = entry;

            UIHudItem result = null;
            try
            {
                result = await AttachCoreAsync<T>(
                    target, assetPath, offset, cancellationToken, generation);
                return result as T;
            }
            finally
            {
                SettleAttach(targetId, entry, result);
            }
        }

        /// <summary>
        /// 实际执行一次 Attach：实例化 → 配置 → 打开 → 世代复检 → 注册映射。
        /// </summary>
        private async UniTask<UIHudItem> AttachCoreAsync<T>(
            Transform target,
            string assetPath,
            Vector2? offset,
            CancellationToken cancellationToken,
            int generation) where T : UIHudItem
        {
            // 实例化 HUD（AssetManager 内部管理对象池）
            var go = await AssetManager.InstantiateAsync(assetPath, _hudContainer, cancellationToken);
            if (go == null)
            {
                LogManager.Error(LogCategories.UIHudManager,
                    "Failed to instantiate HUD: {0} at path: {1}", typeof(T).Name, assetPath);
                return null;
            }

            var hud = go.GetComponent<T>();
            if (hud == null)
            {
                LogManager.Error(LogCategories.UIHudManager,
                    "HUD prefab at '{0}' lacks component '{1}'. Destroying instance.", assetPath, typeof(T).Name);
                AssetManager.DestroyInstance(go);
                return null;
            }

            // 池化实例重新入册：必须在任何可能回收的路径之前复位认领标记，
            // 否则 OnOpen 失败时的 DiscardHud → RecycleHud 会被上一轮的 IsRecycled 挡掉。
            hud.MarkReopened();

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

            // 注册前复检：不同请求串行化后旧实例仍可能留在 map（防御旧状态/第三方 provider 竞态）
            if (_hudMap.TryGetValue(target, out var existing))
            {
                DetachInternal(existing, target);
            }

            // 注册映射
            _hudMap[target] = hud;
            _activeHudList.Add(hud);

            return hud;
        }

        /// <summary>加入一次完全相同的在途 Attach；取消时摘掉自己的等待源。</summary>
        private static async UniTask<UIHudItem> JoinAttachAsync(
            InFlightAttach entry, CancellationToken cancellationToken)
        {
            var tcs = new UniTaskCompletionSource<UIHudItem>();
            if (entry.Joiners == null)
                entry.Joiners = new List<UniTaskCompletionSource<UIHudItem>>(2);

            entry.Joiners.Add(tcs);
            try
            {
                return await tcs.Task.AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                entry.Joiners?.Remove(tcs);
            }
        }

        /// <summary>等待一次不同请求的在途 Attach 结束（结果忽略），随后由调用方重试。</summary>
        private static async UniTask WaitAttachAsync(
            InFlightAttach entry, CancellationToken cancellationToken)
        {
            await JoinAttachAsync(entry, cancellationToken);
        }

        /// <summary>结束一次在途 Attach：按 entry 身份摘账并广播结果（失败/取消为 null）。</summary>
        private void SettleAttach(int targetId, InFlightAttach entry, UIHudItem result)
        {
            // 只删「仍指向本次 entry」的登记：DetachAll 清表后新 Attach 会建新 entry，
            // 旧创建者的 finally 无条件 Remove 会误删新记录。
            if (_inFlightAttach.TryGetValue(targetId, out var current) &&
                ReferenceEquals(current, entry))
            {
                _inFlightAttach.Remove(targetId);
            }

            if (entry.Joiners == null)
                return;

            for (int i = 0; i < entry.Joiners.Count; i++)
                entry.Joiners[i].TrySetResult(result);

            entry.Joiners = null;
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

            // 在途项必须在这里结算：否则加入者会等一个永不再完成的 TCS。
            if (_inFlightAttach.Count > 0)
            {
                var pending = new List<InFlightAttach>(_inFlightAttach.Values);
                _inFlightAttach.Clear();
                for (int i = 0; i < pending.Count; i++)
                {
                    var joiners = pending[i].Joiners;
                    if (joiners == null)
                        continue;

                    for (int j = 0; j < joiners.Count; j++)
                        joiners[j].TrySetResult(null);
                    pending[i].Joiners = null;
                }
            }

            // 收集所有条目（避免遍历中修改字典）
            var entries = new List<(UIHudItem hud, Transform target)>();
            foreach (var kv in _hudMap)
            {
                entries.Add((kv.Value, kv.Key));
            }

            for (int i = 0; i < entries.Count; i++)
            {
                DetachInternal(entries[i].hud, entries[i].target);
            }

            // 清掉不在映射里的孤儿（R4-UIB-002）：并发 Attach 的覆盖、或映射死条目都可能留下
            // 只存在于 _activeHudList 的实例；只遍历 map 会让它继续每帧被驱动。
            if (_activeHudList.Count == 0)
                return;

            var orphans = new List<UIHudItem>(_activeHudList);
            for (int i = 0; i < orphans.Count; i++)
            {
                var hud = orphans[i];
                if (hud == null)
                {
                    _activeHudList.Remove(hud);
                    continue;
                }

                if (!IsInHudMap(hud))
                    DetachInternal(hud, hud.FollowTarget);
            }
        }

        private bool IsInHudMap(UIHudItem hud)
        {
            foreach (var kv in _hudMap)
            {
                if (ReferenceEquals(kv.Value, hud))
                    return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public void Update(float deltaTime, float time)
        {
            if (_activeHudList.Count == 0)
                return;

            // 倒序遍历，防止 HUD 回收时列表收缩导致索引错位
            // 用户 OnUpdate 里可能 HideHud / CloseAllAsync 改写列表（R3-UIB-002）：每轮重新夹紧下标，
            // 并用 lastProcessed 防止「删除下方条目后同一实例移位到当前下标」被同帧驱动两次。
            UIHudItem lastProcessed = null;
            for (int i = _activeHudList.Count - 1; i >= 0; i--)
            {
                if (i >= _activeHudList.Count)
                {
                    i = _activeHudList.Count;   // 夹紧后由 for 的 i-- 落到新的末尾
                    continue;
                }

                var hud = _activeHudList[i];
                if (ReferenceEquals(hud, lastProcessed))
                    continue;   // 回调改写列表后同一实例移到了当前下标

                lastProcessed = hud;

                if (hud == null || !hud.IsOpen)
                {
                    // 已销毁 / 已关闭的 HUD 也把映射条目摘掉，否则它会永远留在 _hudMap 里（UI-B-005）。
                    RemoveMapEntry(hud);
                    _activeHudList.RemoveAt(i);
                    continue;
                }

                // 已停更（UpdateFaulted）的 HUD：OnUpdate 抛过异常，继续驱动只会每帧再抛一次。
                // 但**目标丢失的回收不能停**（R3-UIB-003）：否则 HUD 常驻屏幕、映射永远清不掉。
                if (hud.UpdateFaulted)
                {
                    if (hud.FollowTarget == null)
                        OnHudTargetLost(hud);
                    continue;
                }

                try
                {
                    hud.OnUpdate(deltaTime, time);
                }
                catch (Exception e)
                {
                    // 逐 HUD 隔离（UI-B-001，照面板侧 DriveTier 的先例）：一个 HUD 抛异常不得中断
                    // 本轮遍历——异常逃到外层 provider 隔离后，同帧其余 HUD 会静默停更。
                    // 停更而非继续驱动：典型异常是持续态，继续驱动等于每帧刷一条带栈日志；
                    // 标记随回池复位（UIHudItem.OnPoolRecycle），不会把池化实例变成僵尸。
                    hud.UpdateFaulted = true;
                    LogManager.Exception(LogCategories.UIHudManager, e, string.Format(
                        "HUD '{0}'.OnUpdate threw exception; this HUD will not be driven again until it is recycled " +
                        "(other HUDs are unaffected)", hud.GetType().Name));
                }
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
            {
                // HUD 已被外部销毁：映射条目仍指向它（值不是 ReferenceEquals(null)），
                // 不按 target 摘掉会永远留在表里（UI-B-005）。
                if (target != null)
                    _hudMap.Remove(target);
                return;
            }

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
        /// <para>用 <see cref="object.ReferenceEquals"/> 比较：这里的相等语义是「同一个实例」，
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
                LogManager.Exception(LogCategories.UIHudManager, e, "HUD close failed, recycling anyway");
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
            // 认领守卫（R4-UIB-002）：Detach 的续体、Discard 与 DetachAll 的孤儿清理可能先后
            // 到达同一实例；只有第一次执行 OnPoolRecycle / DestroyInstance。
            if (hud == null || hud.IsRecycled)
                return;

            hud.MarkRecycled();
            try
            {
                // 回池复位停更标记：池化实例不得把「OnUpdate 抛过异常」带进下一次使用
                hud.UpdateFaulted = false;
                hud.OnPoolRecycle();
            }
            catch (Exception e)
            {
                // 回池钩子是用户可覆写点（R4-UIA-002）：异常不得逃逸 fire-and-forget 的
                // Detach 续体，也不得替换 Attach 失败路径的原始 OnOpen 异常。
                LogManager.Exception(LogCategories.UIHudManager, e,
                    "HUD 回池钩子抛异常，已隔离并继续回收。");
            }
            finally
            {
                if (hud.gameObject != null)
                {
                    AssetManager.DestroyInstance(hud.gameObject);
                }
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
        private bool EnsureContainer()
        {
            if (_hudContainer != null)
                return true;

            if (_uiRoot == null)
            {
                LogManager.Error(LogCategories.UIHudManager, "EnsureContainer: uiRoot is null. HUD cannot be created.");
                return false;
            }

            var existing = _uiRoot.Find(HudContainerName);
            if (existing != null)
            {
                _hudContainer = existing;
                _hudContainerCanvas = existing.GetComponent<Canvas>();
                return true;
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
            return true;
        }

        #endregion
    }
}