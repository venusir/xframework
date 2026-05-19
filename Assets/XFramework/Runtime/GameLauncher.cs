using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XLoader;
using XFramework.XUpdate;

namespace XFramework.XCore
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
    /// <para><see cref="BootstrapNode"/> 在 <see cref="OnAwake"/> 中自动添加启动子节点（AssetBootstrapNode、
    /// LockBootstrapNode、MessageBootstrapNode），由 <see cref="NodeUtility.StartupAsync"/> 统一加载调度。</para>
    /// <para>通过 <see cref="UpdateNode"/> 自动管理树中所有 <see cref="XUpdate.IUpdateable"/> 节点的更新。</para>
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

            // BootstrapNode 自动在 OnAwake 中添加 AssetBootstrapNode 等启动子节点
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
            _root?.Destroy();
        }

        #endregion
    }
}