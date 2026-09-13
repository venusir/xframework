using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XNode
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
    /// <para><see cref="ServiceInitializerNode"/> 在 <see cref="OnAwake"/> 中自动添加启动子节点（AssetBootstrapNode），
    /// 由 <see cref="StartupExtensions.StartupAsync"/> 统一启动调度（相位分组执行）。</para>
    /// <para><see cref="UpdateNode"/> 作为节点树中的桥梁，自动将树中 <see cref="XUpdate.IUpdateable"/> 节点注册到
    /// <see cref="UpdateManager"/>（静态服务），统一管理节点树及静态服务的更新需求。</para>
    /// <para>每帧驱动由 <see cref="UpdateManager"/> 注入的 PlayerLoop 系统完成，本类不再参与——
    /// 场景中即使没有 <see cref="GameLauncher"/>，注册到 <see cref="UpdateManager"/> 的对象仍会被派发。</para>
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

        void OnDestroy()
        {
            _root?.Destroy();
        }

        #endregion
    }
}
