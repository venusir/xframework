using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
    /// <para>负责创建 <see cref="RootNode"/> 并调用 <see cref="NodeUtility.StartupAsync"/> 启动节点树。</para>
    /// <para>同时管理 <see cref="Updater"/>，自动注册/注销树中所有 <see cref="IUpdateable"/> 节点。</para>
    /// </summary>
    public class GameLauncher : MonoBehaviour
    {
        #region Private Fields

        /// <summary>当前节点树的根节点。</summary>
        RootNode _root;

        /// <summary>更新调度器，管理所有 <see cref="IUpdateable"/> 节点的更新。</summary>
        Updater _updater;

        #endregion

        #region Lifecycle Methods

        void Awake()
        {
            _updater = new Updater();
            _root = RootNode.Create();
            DontDestroyOnLoad(gameObject);
        }

        async void Start()
        {
            await _root.StartupAsync();

            // 启动后遍历整棵树，注册所有 IUpdateable + 订阅事件
            SubscribeTree(_root);
        }

        void Update()
        {
            _updater.Tick(Time.deltaTime);
        }

        void OnDestroy()
        {
            if (_root != null)
            {
                _root.Destroy();
                _root = null;
            }
            _updater?.Clear();
        }

        #endregion

        #region Updater Event Handlers

        /// <summary>
        /// 递归订阅指定父节点及其所有子节点的事件，并注册 <see cref="IUpdateable"/>。
        /// </summary>
        void SubscribeTree(ParentNode parent)
        {
            parent.OnNodeAdded += OnNodeAdded;
            parent.OnNodeRemoved += OnNodeRemoved;

            for (int i = 0; i < parent.ChildCount; i++)
            {
                var child = parent[i];
                if (child is IUpdateable u)
                    _updater.Register(u, child.Depth);

                if (child is ParentNode childParent)
                    SubscribeTree(childParent);
            }
        }

        /// <summary>
        /// 子节点添加时，注册 <see cref="IUpdateable"/> 并递归订阅其子节点事件。
        /// </summary>
        void OnNodeAdded(BaseNode node)
        {
            if (node is IUpdateable u)
                _updater.Register(u, node.Depth);

            if (node is ParentNode parent)
            {
                parent.OnNodeAdded += OnNodeAdded;
                parent.OnNodeRemoved += OnNodeRemoved;

                // 新加入的 ParentNode 可能已有子节点（如批量添加）
                for (int i = 0; i < parent.ChildCount; i++)
                {
                    var child = parent[i];
                    if (child is IUpdateable u2)
                        _updater.Register(u2, child.Depth);
                    if (child is ParentNode childParent)
                        SubscribeTree(childParent);
                }
            }
        }

        /// <summary>
        /// 子节点移除时，注销 <see cref="IUpdateable"/> 并取消订阅其子节点事件。
        /// <para>利用 <see cref="BaseNode.Destroy"/> 先 RemoveChild 后 DestroyInternal 的时间差，
        /// 此时子节点列表仍可安全遍历。</para>
        /// </summary>
        void OnNodeRemoved(BaseNode node)
        {
            if (node is ParentNode parent)
            {
                // 先递归处理子节点（注销 + 取消订阅）
                for (int i = parent.ChildCount - 1; i >= 0; i--)
                    OnNodeRemoved(parent[i]);

                // 取消订阅该父节点的事件
                parent.OnNodeAdded -= OnNodeAdded;
                parent.OnNodeRemoved -= OnNodeRemoved;
            }

            if (node is IUpdateable u)
                _updater.Unregister(u);
        }

        #endregion
    }
}
