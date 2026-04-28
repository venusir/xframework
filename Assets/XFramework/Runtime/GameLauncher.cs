using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
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

        async void Start()
        {
            await LoadAndStartAsync();
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

        #region Private Methods

        /// <summary>
        /// 加载并启动异步流程。
        /// <para>流程：收集所有 ILoadable → 通过 LoadingManager 统一加载 → 全部完成 → 调用 Root.Start()</para>
        /// </summary>
        async UniTask LoadAndStartAsync()
        {
            // 1. 收集所有 ILoadable 节点
            List<ILoadable> loadables = new List<ILoadable>();
            _root.CollectLoadables(loadables);

            if (loadables.Count == 0)
            {
                // 没有需要加载的节点，直接启动
                _root.Start();
                return;
            }

            // 2. 通过 LoadingManager 统一加载
            //await LoadingMgr.ExecuteLoadAsync(loadables);

            // 3. 全部加载完成，启动节点树
            _root.Start();
        }

        #endregion
    }
}
