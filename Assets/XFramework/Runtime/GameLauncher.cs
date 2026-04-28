using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
    /// <para>负责创建 <see cref="RootNode"/> 并调用 <see cref="NodeUtility.StartupAsync"/> 启动节点树。</para>
    /// <para>通过 <see cref="NodeUpdater"/> 自动管理树中所有 <see cref="IUpdateable"/> 节点的更新。</para>
    /// </summary>
    public class GameLauncher : MonoBehaviour
    {
        #region Private Fields

        RootNode _root;
        NodeUpdater _nodeUpdater;

        #endregion

        #region Lifecycle Methods

        void Awake()
        {
            _nodeUpdater = new NodeUpdater();
            _root = RootNode.Create();
            DontDestroyOnLoad(gameObject);
        }

        async void Start()
        {
            await _root.StartupAsync();
            _nodeUpdater.Bind(_root);
        }

        void Update()
        {
            _nodeUpdater.Tick(Time.deltaTime);
        }

        void OnDestroy()
        {
            _root?.Destroy();
            _nodeUpdater.Dispose();
        }

        #endregion
    }
}
