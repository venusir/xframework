using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUI.View;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 测试用面板实例来源：不触碰 YooAsset，直接在内存里造面板，使 Runtime 测试能打开真实面板。
    /// <para>注入方式：<c>UIManager.PanelFactoryFactory = () =&gt; factory;</c> 后调用 <c>UIManager.Initialize(root)</c>。</para>
    /// <para>回池语义与默认实现对齐（失活 + 脱离父节点，不销毁），以便测试覆盖回池相关行为。</para>
    /// </summary>
    internal sealed class FakePanelFactory : IUIPanelFactory
    {
        #region Fields

        private readonly Dictionary<Type, Func<Transform, UIPanelBase>> _creators
            = new Dictionary<Type, Func<Transform, UIPanelBase>>();

        /// <summary>本工厂造出的全部物体，供 fixture 收尾销毁（假工厂不回池销毁，故必须显式清）。</summary>
        private readonly List<GameObject> _created = new List<GameObject>();

        /// <summary>
        /// 按资源地址索引的实例池，键与 <c>AssetManager</c> 一致（location）。
        /// <para>取用侧必须真的复用：只记录释放而不查询，会让「回池不销毁」这个前提在测试里失效。</para>
        /// </summary>
        private readonly Dictionary<string, Stack<UIPanelBase>> _pool
            = new Dictionary<string, Stack<UIPanelBase>>();

        #endregion

        #region Properties

        /// <summary>创建尝试次数（含未注册类型导致的失败）。</summary>
        public int CreateCount { get; private set; }

        /// <summary>回收次数。</summary>
        public int ReleaseCount { get; private set; }

        /// <summary>当前池中闲置的面板总数。</summary>
        public int PooledCount
        {
            get
            {
                int total = 0;
                foreach (var stack in _pool.Values)
                    total += stack.Count;
                return total;
            }
        }

        /// <summary>
        /// 设置后 <see cref="CreateAsync{T}"/> 会先等这个信号，用于制造「在途打开」窗口以测试并发去重。
        /// <para>置 null 关闭闸门。</para>
        /// </summary>
        public UniTaskCompletionSource<object> Gate { get; set; }

        #endregion

        #region Registration

        /// <summary>
        /// 注册一个面板类型：按类型名建空物体并挂载组件（<see cref="UIViewBase"/> 上的
        /// RequireComponent 会自动补齐 Canvas 与 GraphicRaycaster）。
        /// </summary>
        public void RegisterPanel<T>() where T : UIPanelBase
        {
            RegisterPanel<T>(parent =>
            {
                var go = new GameObject(typeof(T).Name, typeof(RectTransform));
                if (parent != null)
                    go.transform.SetParent(parent, false);

                _created.Add(go);
                return go.AddComponent<T>();
            });
        }

        /// <summary>
        /// 注册自定义构造方式。返回 null 可用来模拟「加载失败」路径。
        /// </summary>
        public void RegisterPanel<T>(Func<Transform, T> creator) where T : UIPanelBase
        {
            _creators[typeof(T)] = creator;
        }

        #endregion

        #region IUIPanelFactory

        /// <inheritdoc/>
        public async UniTask<T> CreateAsync<T>(string assetPath, Transform parent,
            CancellationToken cancellationToken = default) where T : UIPanelBase
        {
            CreateCount++;

            if (Gate != null)
                await Gate.Task.AttachExternalCancellation(cancellationToken);

            // 取用侧复用：命中同地址的闲置实例则直接激活复用，不重新实例化
            if (_pool.TryGetValue(assetPath, out var stack) && stack.Count > 0)
            {
                var pooled = stack.Pop();

                if (pooled is T typed && pooled.gameObject != null)
                {
                    pooled.transform.SetParent(parent, false);
                    pooled.gameObject.SetActive(true);
                    return typed;
                }

                if (pooled != null && pooled.gameObject != null)
                    stack.Push(pooled); // 类型不符：放回去，走新建
            }

            if (!_creators.TryGetValue(typeof(T), out var creator))
            {
                Debug.LogError($"[FakePanelFactory] 未注册的面板类型：{typeof(T).Name}");
                return null;
            }

            return creator(parent) as T;
        }

        /// <inheritdoc/>
        public void Release(UIPanelBase panel)
        {
            ReleaseCount++;

            if (panel == null || panel.gameObject == null)
                return;

            // 对齐 AssetManager 的回池语义：失活 + 脱离父节点，不销毁
            panel.gameObject.SetActive(false);
            panel.transform.SetParent(null, false);

            // 按地址入池，供下次同地址的 CreateAsync 复用。地址未标记（异常回滚的早期路径）
            // 时不入池，留在 _created 里由 fixture 收尾销毁。
            var location = panel.AssetPath;
            if (string.IsNullOrEmpty(location))
                return;

            if (!_pool.TryGetValue(location, out var stack))
            {
                stack = new Stack<UIPanelBase>();
                _pool[location] = stack;
            }

            stack.Push(panel);
        }

        #endregion

        #region Cleanup

        /// <summary>销毁本工厂造出的全部物体（fixture TearDown 调用）。</summary>
        public void DestroyAll()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    UnityEngine.Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
            _pool.Clear();
        }

        #endregion
    }
}
