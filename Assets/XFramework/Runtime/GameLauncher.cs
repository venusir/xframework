using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XCore;
using XFramework.XLoader;
using XFramework.XUpdate;

namespace XFramework.XCore
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
    /// <para><see cref="BootstrapNode"/> 自动管理所有非节点模块（AssetManager、LockManager、
    /// MessageManager 等）的初始化，向 <see cref="NodeUtility.StartupAsync"/> 报告统一进度。</para>
    /// <para>通过 <see cref="UpdateNode"/> 自动管理树中所有 <see cref="IUpdateable"/> 节点的更新。</para>
    /// </summary>
    public class GameLauncher : MonoBehaviour
    {
        #region Private Fields

        RootNode _root;
        UpdateNode _updateService;

        #endregion

        #region Lifecycle Methods

        void Awake()
        {
            _root = RootNode.Create();
            _updateService = _root.AddNode<UpdateNode>();

            // BootstrapNode 统一管理所有非节点模块的初始化与销毁
            _root.AddNode<BootstrapNode>();

            DontDestroyOnLoad(gameObject);
        }

        async void Start()
        {
            // 启动节点树：BootstrapNode 会最先执行，依次初始化 AssetManager 等模块
            await _root.StartupAsync();
        }

        void Update()
        {
            _updateService.Tick(Time.time);
        }

        void OnDestroy()
        {
            // BootstrapNode 的 OnDestroy 会反向销毁所有模块（AssetManager 等）
            _root?.Destroy();
        }

        #endregion
    }
}