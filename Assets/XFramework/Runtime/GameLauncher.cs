using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
    /// <para>负责创建 <see cref="RootNode"/> 并启动节点树。</para>
    /// <para><see cref="LoadingManager"/> 按需由开发者自行组装到节点树中。</para>
    /// </summary>
    public class GameLauncher : MonoBehaviour
    {
        /// <summary>
        /// 当前节点树的根节点。
        /// </summary>
        RootNode _root;

        #region Lifecycle Methods

        void Awake()
        {
            _root = RootNode.Create();
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            _root.Start();
        }

        void OnDestroy()
        {
            if (_root != null)
            {
                _root.Destroy();
                _root = null;
            }
        }

        #endregion
    }
}
