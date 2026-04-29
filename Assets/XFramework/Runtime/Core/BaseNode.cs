using System;

namespace XFramework
{
    /// <summary>
    /// 树节点系统的核心接口，定义了节点的基本契约。
    /// </summary>
    public interface IBaseNode
    {
        /// <summary>销毁节点。</summary>
        void Destroy();
    }

    /// <summary>
    /// 树节点系统的抽象基类。
    /// <para>提供深度管理、父子关系、生命周期（Awake/Destroy/Start）等核心功能。</para>
    /// </summary>
    public abstract class BaseNode : IBaseNode
    {
        #region Private Properties

        /// <summary>节点在树中的深度（根节点为 0）。</summary>
        int _depth;

        /// <summary>是否已执行过 Start，防止重复调用。</summary>
        bool _started;

        /// <summary>节点是否已被销毁。</summary>
        bool _destroyed;

        /// <summary>父节点引用，根节点为 null。</summary>
        ParentNode _parent;

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
        /// 初始化节点。在节点创建后显式调用。
        /// <para>替代在构造函数中调用虚方法，避免 C# 构造函数调用虚方法的 anti-pattern。</para>
        /// <para>通常由 <see cref="Create{T}"/> 或 <see cref="ParentNode.AddChild"/> 自动调用。</para>
        /// </summary>
        internal void Awake()
        {
            AwakeInternal();
        }

        /// <summary>
        /// 设置父节点并更新深度。
        /// </summary>
        /// <param name="parent">新的父节点，null 表示成为根节点。</param>
        internal void SetParent(ParentNode parent)
        {
            if (_parent != parent)
            {
                _parent = parent;
                _depth = _parent != null ? _parent._depth + 1 : 0;
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

            OnAwake();
        }

        /// <summary>
        /// 内部销毁方法。由 <see cref="Destroy"/> 调用。
        /// <para>派生类可 override 此方法添加自定义销毁逻辑，但必须调用 base.DestroyInternal()。</para>
        /// </summary>
        internal virtual void DestroyInternal()
        {
            if (_destroyed) return;

            OnDestroy();

            _depth = 0;
            _parent = null;
            _destroyed = true;

            // 通知缓存池：节点已销毁，可以回收
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

            // 通知外部（如 UpdateBinder）：节点已启动，可以开始接收 Update
            OnStarted?.Invoke(this);
        }

        #endregion

        #region Virtual Callbacks

        /// <summary>
        /// 节点初始化时的回调。在 <see cref="AwakeInternal"/> 末尾调用。
        /// </summary>
        protected virtual void OnAwake() { }

        /// <summary>
        /// 节点销毁时的回调。在 <see cref="DestroyInternal"/> 末尾调用。
        /// </summary>
        protected virtual void OnDestroy() { }

        /// <summary>
        /// 节点启动时的回调。类似于 Unity 的 Start 方法，
        /// 在 Awake 完成且所有组件添加完毕后触发。
        /// <para>在 <see cref="StartInternal"/> 末尾调用。</para>
        /// </summary>
        protected virtual void OnStart() { }

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

        #region Pooling


        /// <summary>
        /// 节点销毁完成时触发，用于通知缓存池回收节点。
        /// <para>由 <see cref="NodePool{T}"/> 内部订阅使用。</para>
        /// </summary>
        internal event Action<BaseNode> OnReturnToPool;

        /// <summary>
        /// 节点启动完成时触发，用于通知外部（如 <see cref="UpdateBinder"/>）节点已可接收 Update。
        /// </summary>
        internal event Action<BaseNode> OnStarted;

        #endregion
    }
}
