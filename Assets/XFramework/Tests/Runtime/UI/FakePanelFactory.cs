using System;
using System.Collections.Generic;
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

        /// <summary>回池的面板（对齐 AssetManager 语义：只失活，不销毁）。</summary>
        private readonly List<UIPanelBase> _pool = new List<UIPanelBase>();

        #endregion

        #region Properties

        /// <summary>创建尝试次数（含未注册类型导致的失败）。</summary>
        public int CreateCount { get; private set; }

        /// <summary>回收次数。</summary>
        public int ReleaseCount { get; private set; }

        /// <summary>当前池中的面板。</summary>
        public IReadOnlyList<UIPanelBase> Pool => _pool;

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
        public UniTask<T> CreateAsync<T>(string assetPath, Transform parent) where T : UIPanelBase
        {
            CreateCount++;

            if (!_creators.TryGetValue(typeof(T), out var creator))
            {
                Debug.LogError($"[FakePanelFactory] 未注册的面板类型：{typeof(T).Name}");
                return UniTask.FromResult<T>(null);
            }

            return UniTask.FromResult(creator(parent) as T);
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
            _pool.Add(panel);
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
