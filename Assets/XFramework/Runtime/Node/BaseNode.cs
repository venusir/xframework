using System;
using System.Collections.Generic;
using System.Threading;

namespace XFramework.XNode
{

    /// <summary>
    /// 树节点系统的核心接口，定义了节点的基本契约。
    /// </summary>
    public interface IBaseNode
    {
        /// <summary>销毁节点。</summary>
        void Destroy();

        /// <summary>节点 Start 完成时触发。</summary>
        event Action<BaseNode> OnNodeStarted;
    }

    /// <summary>
    /// 提供销毁时的 CancellationToken，用于自动取消订阅和释放资源。
    /// <para>类似于 MonoBehaviour.destroyCancellationToken。</para>
    /// <para>实现此接口后，通过 <see cref="MessageBus"/> 的扩展方法订阅消息时，
    /// 订阅会自动绑定到对象的生命周期，对象销毁时自动取消订阅。</para>
    /// </summary>
    public interface IDestroyCancellationToken
    {
        /// <summary>
        /// 对象销毁时的 CancellationToken。绑定到此 Token 的订阅会在对象销毁时自动取消。
        /// </summary>
        CancellationToken DestroyCancellationToken { get; }
    }

    /// <summary>
    /// 树节点系统的抽象基类。
    /// <para>提供深度管理、父子关系、生命周期（Awake/Destroy/Start）等核心功能。</para>
    /// <para>实现 <see cref="IDisposable"/>，支持 <c>using</c> 语法和 <c>AddTo</c> 扩展。</para>
    /// </summary>
    public abstract class BaseNode : IBaseNode, IDestroyCancellationToken, IDisposable
    {
        #region Private Properties

        /// <summary>节点在树中的深度（根节点为 0）。</summary>
        int _depth;

        /// <summary>是否已执行过 Start，防止重复调用。</summary>
        bool _started;

        /// <summary>节点是否已被销毁。</summary>
        bool _destroyed;

        /// <summary>节点是否启用。禁用后跳过 Update 和事件响应。</summary>
        bool _enabled = true;

        /// <summary>
        /// 级联活跃状态。仅当自身 Enabled 为 true 且所有祖先节点均活跃时为 true。
        /// <para>用于快速判断节点是否处于活跃状态。</para>
        /// </summary>
        bool _active = true;

        /// <summary>父节点引用，根节点为 null。</summary>
        ParentNode _parent;

        /// <summary>节点销毁时的 CancellationTokenSource，用于自动取消订阅。</summary>
        CancellationTokenSource _destroyCts;

        /// <summary>
        /// 通过 <see cref="NodeExtensions.AddToNode{T}(T, BaseNode)"/> 绑定的 Disposable 列表。
        /// <para>节点销毁时统一 Dispose，避免 CancellationToken.Register 的逐个分配开销。</para>
        /// </summary>
        List<IDisposable> _autoDisposables;

        /// <summary>节点关联的标签集合。延迟初始化以节约内存。</summary>
        HashSet<string> _tags;

        #endregion

        #region Public Methods

        /// <summary>
        /// 启动节点。应在 Awake 完成、所有组件已添加完毕后显式调用。
        /// <para>调用链: Start() → StartInternal() → OnStart()</para>
        /// <para>调用 Start 后，_started 置为 true，后续重复调用无效。</para>
        /// </summary>
        internal void Start()
        {
            StartInternal();
        }

        /// <summary>
        /// 销毁节点。如果已销毁则直接返回。
        /// <para>销毁前会自动从父节点脱离（如果存在父节点），确保父节点的子节点列表不再持有此节点引用。</para>
        /// <para>调用链: Destroy() → RemoveChild()（从父节点脱离）→ DestroyInternal() → OnDestroy()</para>
        /// </summary>
        public void Destroy()
        {
            if (_destroyed) return;

            // 销毁前先从父节点脱离（RemoveChild 会从父节点的子节点列表中移除并触发 OnNodeRemoved 事件）
            if (_parent != null)
            {
                var parent = _parent;
                _parent = null;
                parent.RemoveChild(this, fromChild: true);
            }

            DestroyInternal();
        }

        /// <summary>
        /// 释放所有资源。等价于 <see cref="Destroy()"/>。
        /// <para>实现 <see cref="IDisposable"/> 以支持 <c>using</c> 语法和 <c>AddTo</c> 扩展。</para>
        /// </summary>
        public void Dispose() => Destroy();

        #endregion

        #region Internal Methods

        internal ParentNode Parent => _parent;

        /// <summary>
        /// 节点在树中的深度（根节点为 0）。
        /// </summary>
        internal int Depth => _depth;

        /// <summary>
        /// 节点是否已被销毁。
        /// </summary>
        internal bool Destroyed => _destroyed;

        /// <summary>
        /// 获取节点是否已执行过 Start。
        /// </summary>
        internal bool Started => _started;

        /// <summary>
        /// 节点是否启用。禁用后跳过 Update 和事件响应。
        /// <para>默认为 true。设置为 false 后，Update 系统将跳过此节点。</para>
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set => SetEnabled(value);
        }

        /// <summary>
        /// 节点是否处于级联活跃状态（自身启用且所有祖先节点均活跃）。
        /// <para>用于快速判断节点是否应接收 Update、事件等。</para>
        /// </summary>
        public bool Active => _active;

        /// <summary>
        /// 初始化节点。在节点创建后显式调用。
        /// <para>替代在构造函数中调用虚方法，避免 C# 构造函数调用虚方法的 anti-pattern。</para>
        /// <para>通常由 <see cref="Create{T}"/> 或 <see cref="ParentNode.AddChild"/> 自动调用。</para>
        /// </summary>
        internal void Awake()
        {
            AwakeInternal();
        }

        /// <summary>
        /// 设置父节点并更新深度和级联活跃状态。
        /// </summary>
        /// <param name="parent">新的父节点，null 表示成为根节点。</param>
        internal void SetParent(ParentNode parent)
        {
            if (_parent != parent)
            {
                _parent = parent;
                _depth = _parent != null ? _parent._depth + 1 : 0;

                // 根据新父节点更新级联活跃状态
                RefreshActive();
            }
        }

        /// <summary>
        /// 刷新当前节点及其所有子节点的级联活跃状态。
        /// <para>当自身或祖先节点启用状态变化时调用。</para>
        /// </summary>
        internal virtual void RefreshActive()
        {
            bool parentActive = _parent == null || _parent._active;
            bool newActive = _enabled && parentActive;

            if (_active != newActive)
            {
                _active = newActive;
                if (_started && !_destroyed)
                {
                    if (newActive)
                        OnEnable();
                    else
                        OnDisable();
                }
            }
        }

        /// <summary>
        /// 内部初始化方法。由 <see cref="Awake"/> 调用。
        /// <para>派生类可 override 此方法添加自定义初始化逻辑，但必须调用 base.AwakeInternal()。</para>
        /// </summary>
        internal virtual void AwakeInternal()
        {
            _depth = 0;
            _parent = null;
            _destroyed = false;
            _started = false;
            _enabled = true;
            _active = true;
            _destroyCts = new CancellationTokenSource();

            OnAwake();
        }

        /// <summary>
        /// 内部销毁方法。由 <see cref="Destroy"/> 调用。
        /// <para>分为三个阶段：</para>
        /// <para>Phase 1 — 标记销毁 + 取消令牌 + 清理 auto-disposables + 通知外部即将销毁。</para>
        /// <para>Phase 2 — 调用 <see cref="OnDestroy"/> 用户清理回调（此时树引用仍有效，可安全查询）。</para>
        /// <para>Phase 3 — 清理内部引用 + 通知缓存池回收。</para>
        /// <para>派生类可 override 此方法添加自定义销毁逻辑，但必须调用 base.DestroyInternal()。</para>
        /// </summary>
        internal virtual void DestroyInternal()
        {
            if (_destroyed) return;

            // ===== Phase 1: 标记销毁 + 取消令牌 + 清理 auto-disposables + 通知外部 =====
            _destroyed = true;

            if (_destroyCts != null)
            {
                _destroyCts.Cancel();
                _destroyCts.Dispose();
                _destroyCts = null;
            }

            // 统一 Dispose _autoDisposables 列表中的所有 disposable
            if (_autoDisposables != null)
            {
                for (int i = 0; i < _autoDisposables.Count; i++)
                {
                    _autoDisposables[i].Dispose();
                }
                _autoDisposables.Clear();
                _autoDisposables = null;
            }

            OnNodeDestroy?.Invoke(this);

            // ===== Phase 2: 用户自定义清理（树引用仍有效） =====
            OnDestroy();

            // ===== Phase 3: 清理内部引用 + 通知缓存池回收 =====
            // 清理标签
            if (_tags != null)
            {
                _tags.Clear();
                _tags = null;
            }

            _depth = 0;
            _parent = null;

            OnReturnToPool?.Invoke(this);
        }

        /// <summary>
        /// 内部启动方法。由 <see cref="Start"/> 调用。
        /// <para>派生类可 override 此方法添加自定义启动逻辑，但必须调用 base.StartInternal()。</para>
        /// </summary>
        internal virtual void StartInternal()
        {
            if (Started || _destroyed) return;

            _started = true;

            OnStart();

            // 通知外部：节点 Start 完成
            OnNodeStarted?.Invoke(this);

            // 若节点初始即处于活跃状态，补调首次 OnEnable。
            // RefreshActive 的 _active != newActive guard 在 _active 初始为 true
            // 且重新计算后仍为 true 时会跳过，需在此补齐，与 Unity 行为一致。
            if (_active)
                OnEnable();
        }

        #endregion

        #region Virtual Callbacks

        /// <summary>
        /// 节点初始化时的回调。在 <see cref="AwakeInternal"/> 末尾调用。
        /// </summary>
        protected virtual void OnAwake() { }

        /// <summary>
        /// 节点销毁时的回调。在 <see cref="DestroyInternal"/> Phase 2 调用。
        /// <para>此时树引用仍有效，可安全访问父子节点。</para>
        /// </summary>
        protected virtual void OnDestroy() { }

        /// <summary>
        /// 节点启动时的回调。类似于 Unity 的 Start 方法，
        /// 在 Awake 完成且所有组件添加完毕后触发。
        /// <para>在 <see cref="StartInternal"/> 末尾调用。</para>
        /// </summary>
        protected virtual void OnStart() { }

        /// <summary>
        /// 节点激活时的回调。当节点的级联活跃状态 <see cref="Active"/> 从 false 变为 true 时触发。
        /// <para>可能由自身启用或祖先节点激活引起，语义与 Unity MonoBehaviour.OnEnable 一致。</para>
        /// <para>仅在节点已 Start 且未销毁时触发。</para>
        /// </summary>
        protected virtual void OnEnable() { }

        /// <summary>
        /// 节点失活时的回调。当节点的级联活跃状态 <see cref="Active"/> 从 true 变为 false 时触发。
        /// <para>可能由自身禁用或祖先节点失活引起，语义与 Unity MonoBehaviour.OnDisable 一致。</para>
        /// <para>仅在节点已 Start 且未销毁时触发。</para>
        /// </summary>
        protected virtual void OnDisable() { }

        #endregion

        #region Enable / Disable

        /// <summary>
        /// 设置启用状态。修改 <c>_enabled</c> 后通过 <see cref="RefreshActive"/> 统一计算 <c>_active</c> 并触发回调，
        /// 无论状态变化由自身还是祖先引起，回调语义一致。
        /// <para>Start 前也可设置，<c>_enabled</c> 和 <c>_active</c> 会立即更新，但 <see cref="OnEnable"/> / <see cref="OnDisable"/> 回调
        /// 只在 <c>_started == true</c> 时触发，与 Unity MonoBehaviour 行为一致。</para>
        /// </summary>
        /// <param name="value">目标启用状态。</param>
        void SetEnabled(bool value)
        {
            if (_enabled == value) return;
            if (_destroyed) return;

            _enabled = value;

            // RefreshActive 会立即更新 _active，但回调保护在 RefreshActive 内部（_started && !_destroyed）
            RefreshActive();
        }

        #endregion

        #region Tags

        /// <summary>
        /// 节点关联的所有标签（只读）。
        /// <para>用于分类、筛选节点，支持按标签批量查询。</para>
        /// </summary>
        public IReadOnlyCollection<string> Tags => _tags;

        /// <summary>
        /// 添加标签。同一标签重复添加无副作用（HashSet 去重）。
        /// <para>标签延迟初始化，未添加过标签的节点不分配额外内存。</para>
        /// </summary>
        /// <param name="tag">要添加的标签。</param>
        public void AddTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            _tags ??= new HashSet<string>();
            _tags.Add(tag);
        }

        /// <summary>
        /// 移除标签。
        /// </summary>
        /// <param name="tag">要移除的标签。</param>
        public void RemoveTag(string tag)
        {
            _tags?.Remove(tag);
        }

        /// <summary>
        /// 是否拥有指定标签。
        /// </summary>
        /// <param name="tag">要检查的标签。</param>
        /// <returns>如果拥有该标签则返回 true。</returns>
        public bool HasTag(string tag)
        {
            return _tags != null && _tags.Contains(tag);
        }

        /// <summary>
        /// 是否拥有所有指定标签（AND 逻辑）。
        /// </summary>
        /// <param name="tags">要检查的标签数组。</param>
        /// <returns>如果拥有所有指定标签则返回 true，参数为 null 或空数组返回 false。</returns>
        public bool HasTags(params string[] tags)
        {
            if (tags == null || tags.Length == 0) return false;
            if (_tags == null) return false;
            for (int i = 0; i < tags.Length; i++)
            {
                if (!_tags.Contains(tags[i])) return false;
            }
            return true;
        }

        #endregion

        #region Init

        /// <summary>
        /// 参数初始化。在 Awake 之前调用，相当于构造函数的替代。
        /// 子类重写 <see cref="OnInit(object)"/> 来接收参数化初始化数据。
        /// </summary>
        /// <param name="arg">初始化参数。</param>
        internal void Init(object arg)
        {
            OnInit(arg);
        }

        /// <summary>
        /// 参数初始化回调。在 Awake 之前触发。
        /// <para>子类重写此方法以接收 <see cref="NodeFactory.GetNode{T}(object)"/> 传入的参数。</para>
        /// </summary>
        /// <param name="arg">初始化参数。</param>
        protected virtual void OnInit(object arg) { }

        #endregion

        #region Service Resolution

        /// <summary>
        /// 沿父链向上遍历，在所有祖先 EntityNode 中查找第一个匹配指定接口类型的节点。
        /// <para>通常用于获取挂载在 RootNode 下的全局服务。</para>
        /// </summary>
        /// <typeparam name="T">要查找的接口类型，必须实现 IBaseNode。</typeparam>
        /// <returns>找到的节点，未找到则返回 null。</returns>
        protected T Get<T>() where T : IBaseNode
        {
            BaseNode current = Parent;
            while (current != null)
            {
                if (current is EntityNode entity)
                {
                    var component = entity.GetNode<T>(false);
                    if (component != null)
                        return component;
                }
                current = current.Parent;
            }
            return default;
        }

        #endregion

        #region Events

        /// <summary>
        /// 节点销毁完成时触发，用于通知缓存池回收节点。
        /// <para>由 <see cref="NodePool{T}"/> 内部订阅使用。</para>
        /// </summary>
        internal event Action<BaseNode> OnReturnToPool;

        /// <summary>
        /// 节点 Start 完成时触发。
        /// </summary>
        public event Action<BaseNode> OnNodeStarted;

        /// <summary>
        /// 节点销毁时触发。用于响应式扩展中自动取消订阅。
        /// </summary>
        public event Action<BaseNode> OnNodeDestroy;

        #endregion

        #region CancellationToken

        /// <summary>
        /// 节点销毁时的 CancellationToken。绑定到此 Token 的订阅会在节点销毁时自动取消。
        /// <para>类似于 MonoBehaviour.destroyCancellationToken。</para>
        /// <para>使用方式: <c>disposable.AddTo(node.DestroyCancellationToken)</c></para>
        /// </summary>
        public CancellationToken DestroyCancellationToken => _destroyCts?.Token ?? CancellationToken.None;

        #endregion

        #region Auto Disposables

        /// <summary>
        /// 将 <paramref name="disposable"/> 注册到此节点的自动清理列表。
        /// <para>节点销毁时会自动调用 <see cref="IDisposable.Dispose()"/>。</para>
        /// <para>如果节点已销毁，则立即 Dispose。</para>
        /// </summary>
        /// <param name="disposable">要管理的 disposable。</param>
        internal void RegisterAutoDispose(IDisposable disposable)
        {
            if (_destroyed)
            {
                disposable.Dispose();
                return;
            }

            _autoDisposables ??= new List<IDisposable>();
            _autoDisposables.Add(disposable);
        }

        /// <summary>
        /// 从自动清理列表中移除 <paramref name="disposable"/>。
        /// </summary>
        /// <param name="disposable">要移除的 disposable。</param>
        internal void UnregisterAutoDispose(IDisposable disposable)
        {
            _autoDisposables?.Remove(disposable);
        }

        #endregion
    }
}