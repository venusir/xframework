using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
    /// <para>负责创建 <see cref="RootNode"/> 并调用 <see cref="NodeUtility.StartupAsync"/> 启动节点树。</para>
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
            DontDestroyOnLoad(gameObject);
        }

        async void Start()
        {
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
