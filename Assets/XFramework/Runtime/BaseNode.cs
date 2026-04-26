using System;

namespace XFramework
{
    /// <summary>
    /// 树节点系统的核心接口，定义了节点的基本契约。
    /// </summary>
    public interface IBaseNode
    {
        /// <summary>节点在树中的深度（根节点为 0）。</summary>
        int Depth { get; }

        /// <summary>节点是否已被销毁。</summary>
        bool Destroyed { get; }

        /// <summary>父节点引用，根节点为 null。</summary>
        ParentNode Parent { get; }
    }

    /// <summary>
    /// 树节点系统的抽象基类。
    /// <para>提供深度管理、父子关系、生命周期（Awake/Destroy/Start）等核心功能。</para>
    /// </summary>
    public abstract class BaseNode : IBaseNode
    {
        #region Public Properties

        /// <summary>节点在树中的深度（根节点为 0）。</summary>
        public int Depth { get; private set; }

        /// <summary>节点是否已被销毁。</summary>
        public bool Destroyed { get; private set; }

        /// <summary>父节点引用，根节点为 null。</summary>
        public ParentNode Parent { get; private set; }

        /// <summary>是否为根节点（没有父节点）。</summary>
        public bool IsRoot => Parent == null;

        #endregion

        #region Static Methods

        /// <summary>
        /// 创建并初始化一个节点。
        /// <para>这是创建独立节点的推荐方式，确保 <see cref="OnAwake"/> 被正确调用。</para>
        /// </summary>
        /// <typeparam name="T">节点类型，必须有无参构造函数。</typeparam>
        /// <returns>已初始化的节点实例。</returns>
        public static T Create<T>() where T : BaseNode, new()
        {
            T node = new T();
            node.Awake();
            return node;
        }

        #endregion

        #region Lifecycle Methods

        /// <summary>
        /// 初始化节点。在节点创建后显式调用。
        /// <para>替代在构造函数中调用虚方法，避免 C# 构造函数调用虚方法的 anti-pattern。</para>
        /// <para>通常由 <see cref="Create{T}"/> 或 <see cref="ParentNode.AddChild"/> 自动调用。</para>
        /// </summary>
        internal void Awake()
        {
            if (Destroyed)
            {
                throw new InvalidOperationException("Cannot initialize a destroyed node.");
            }

            AwakeInternal();
        }

        /// <summary>
        /// 销毁节点。如果已销毁则直接返回。
        /// <para>调用链: Destroy() → DestroyInternal() → OnDestroyed()</para>
        /// </summary>
        public void Destroy()
        {
            if (Destroyed) return;

            DestroyInternal();
        }

        /// <summary>
        /// 启动节点。应在 Awake 完成、所有组件已添加完毕后显式调用。
        /// <para>调用链: Start() → StartInternal() → OnStart()</para>
        /// <para>调用 Start 后，_started 置为 true，后续重复调用无效。</para>
        /// </summary>
        public void Start()
        {
            if (_started || Destroyed) return;
            _started = true;
            StartInternal();
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// 设置父节点并更新深度。
        /// </summary>
        /// <param name="parent">新的父节点，null 表示成为根节点。</param>
        internal void SetParent(ParentNode parent)
        {
            if (Parent != parent)
            {
                Parent = parent;
                Depth = parent?.Depth + 1 ?? 0;
            }
        }

        /// <summary>
        /// 内部初始化方法。由 <see cref="Awake"/> 调用。
        /// <para>派生类可 override 此方法添加自定义初始化逻辑，但必须调用 base.AwakeInternal()。</para>
        /// </summary>
        internal virtual void AwakeInternal()
        {
            Depth = 0;
            Parent = null;
            Destroyed = false;

            OnAwake();
        }

        /// <summary>
        /// 内部销毁方法。由 <see cref="Destroy"/> 调用。
        /// <para>派生类可 override 此方法添加自定义销毁逻辑，但必须调用 base.DestroyInternal()。</para>
        /// </summary>
        internal virtual void DestroyInternal()
        {
            OnDestroyed();

            Depth = 0;
            Parent = null;
            Destroyed = true;
        }

        /// <summary>
        /// 内部启动方法。由 <see cref="Start"/> 调用。
        /// <para>派生类可 override 此方法添加自定义启动逻辑，但必须调用 base.StartInternal()。</para>
        /// </summary>
        internal virtual void StartInternal()
        {
            OnStart();
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
        protected virtual void OnDestroyed() { }

        /// <summary>
        /// 节点启动时的回调。类似于 Unity 的 Start 方法，
        /// 在 Awake 完成且所有组件添加完毕后触发。
        /// <para>在 <see cref="StartInternal"/> 末尾调用。</para>
        /// </summary>
        protected virtual void OnStart() { }

        #endregion

        #region Internal Properties

        /// <summary>是否已执行过 Start，防止重复调用。</summary>
        internal bool Started => _started;

        #endregion

        #region Private Fields

        /// <summary>是否已执行过 Start，防止重复调用。</summary>
        private bool _started;

        #endregion
    }
}
