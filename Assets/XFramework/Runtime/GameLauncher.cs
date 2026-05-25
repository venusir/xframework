using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLoader;
using XFramework.XUpdate;

namespace XFramework.XNode
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
    /// <para><see cref="ServiceInitializerNode"/> 在 <see cref="OnAwake"/> 中自动添加启动子节点（AssetBootstrapNode），
    /// 由 <see cref="NodeUtility.StartupAsync"/> 统一加载调度。</para>
    /// <para><see cref="UpdateNode"/> 作为节点树中的桥梁，自动将树中 <see cref="XUpdate.IUpdateable"/> 节点注册到
    /// <see cref="UpdateManager"/>（静态服务），统一管理节点树及静态服务的更新需求。</para>
    /// <para>每帧通过 <see cref="UpdateManager.Tick(float)"/> 驱动所有已注册的更新对象。</para>
    /// </summary>
    public class GameLauncher : MonoBehaviour
    {
        #region Private Fields

        RootNode _root;

        #endregion

        #region Lifecycle Methods

        void Awake()
        {
            _root = RootNode.Create();

            // UpdateNode 作为节点树到 UpdateManager 的桥梁，自动监听树的增删事件
            _root.AddNode<UpdateNode>();

            // ServiceInitializerNode 自动在 OnAwake 中添加 AssetBootstrapNode 启动子节点
            _root.AddNode<ServiceInitializerNode>();

            DontDestroyOnLoad(gameObject);
        }

        async void Start()
        {
            // 启动节点树：ServiceInitializerNode 会最先执行，依次初始化 AssetManager 等模块
            await _root.StartupAsync();
        }

        void Update()
        {
            // 统一通过 UpdateManager 驱动所有已注册的更新（包括节点树节点和静态服务）
            UpdateManager.Tick(Time.time);
        }

        void OnDestroy()
        {
            _root?.Destroy();
        }

        #endregion
    }
}
