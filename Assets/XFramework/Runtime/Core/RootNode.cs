using System;
using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 根节点接口。作为节点树的入口，提供便捷的节点创建方法。
    /// </summary>
    public interface IRootNode : IEntityNode
    {
        /// <summary>创建并添加一个子节点。</summary>
        T CreateNode<T>() where T : BaseNode, new();

        /// <summary>创建并添加一个子节点，并传入初始化参数。</summary>
        T CreateNode<T>(object arg) where T : BaseNode, new();
    }

    /// <summary>
    /// 根节点。作为节点树的入口，提供便捷的节点创建方法。
    /// </summary>
    public class RootNode : EntityNode, IRootNode
    {
        #region Static Methods

        /// <summary>
        /// 创建根节点。
        /// <para>通过 <see cref="NodeFactory"/> 获取实例并自动调用 <see cref="BaseNode.Awake"/> 完成初始化。</para>
        /// </summary>
        /// <returns>已初始化的根节点实例。</returns>
        public static RootNode Create()
        {
            RootNode root = NodeFactory.GetNode<RootNode>();
            root.Awake();
            return root;
        }

        /// <summary>
        /// 创建根节点，并传入初始化参数。
        /// <para>通过 <see cref="NodeFactory"/> 获取实例，设置参数后自动调用 <see cref="BaseNode.Awake"/> 完成初始化。</para>
        /// </summary>
        /// <param name="arg">初始化参数。</param>
        /// <returns>已初始化的根节点实例。</returns>
        public static RootNode Create(object arg)
        {
            RootNode root = NodeFactory.GetNode<RootNode>(arg);
            root.Awake();
            return root;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 创建并添加一个子节点。
        /// <para>通过 <see cref="NodeFactory"/> 获取节点实例，自动添加到当前根节点并完成初始化。</para>
        /// <para>如果同类型节点已存在，则直接返回已有节点。</para>
        /// </summary>
        /// <typeparam name="T">子节点类型，必须有无参构造函数。</typeparam>
        /// <returns>已添加的子节点实例。</returns>
        public T CreateNode<T>() where T : BaseNode, new()
        {
            return AddComponent<T>();
        }

        /// <summary>
        /// 创建并添加一个子节点，并传入初始化参数。
        /// <para>如果同类型节点已存在，则直接返回已有节点。</para>
        /// </summary>
        /// <typeparam name="T">子节点类型，必须有无参构造函数。</typeparam>
        /// <param name="arg">初始化参数。</param>
        /// <returns>已添加的子节点实例。</returns>
        public T CreateNode<T>(object arg) where T : BaseNode, new()
        {
            return AddComponent<T>(arg);
        }

        /// <summary>
        /// 创建并添加一个子节点，并立即执行异步启动。
        /// <para>节点会先挂入树（此时可访问 RootNode 上的服务），再执行异步加载管线，
        /// 加载完成后自动触发 <see cref="BaseNode.Start"/>。</para>
        /// </summary>
        /// <typeparam name="T">子节点类型，必须有无参构造函数且实现 <see cref="IParentNode"/>。</typeparam>
        /// <returns>已添加并完成异步启动的子节点实例。</returns>
        public async UniTask<T> CreateNodeAsync<T>() where T : BaseNode, IParentNode, new()
        {
            T node = NodeFactory.GetNode<T>();
            AddChild(node, deferStart: true);
            await ((IParentNode)node).StartupAsync();
            return node;
        }

        /// <summary>
        /// 创建并添加一个子节点（带参数），并立即执行异步启动。
        /// </summary>
        /// <typeparam name="T">子节点类型，必须有无参构造函数且实现 <see cref="IParentNode"/>。</typeparam>
        /// <param name="arg">初始化参数。</param>
        /// <returns>已添加并完成异步启动的子节点实例。</returns>
        public async UniTask<T> CreateNodeAsync<T>(object arg) where T : BaseNode, IParentNode, new()
        {
            T node = NodeFactory.GetNode<T>(arg);
            AddChild(node, deferStart: true);
            await ((IParentNode)node).StartupAsync();
            return node;
        }

        #endregion
    }
}
