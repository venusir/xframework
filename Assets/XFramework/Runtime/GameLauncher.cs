using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
    /// <para>全局单例，挂载在场景中的 GameObject 上，负责创建和管理 <see cref="RootNode"/> 的生命周期。</para>
    /// <para>内部持有 <see cref="LoadingManager"/>，通过 <see cref="LoadingMgr"/> 属性暴露给外部直接使用。</para>
    /// </summary>
    public class GameLauncher : MonoBehaviour
    {
        #region Public Properties

        /// <summary>
        /// 全局单例实例。
        /// </summary>
        public static GameLauncher Instance { get; private set; }

        /// <summary>
        /// 当前节点树的根节点。
        /// </summary>
        public RootNode Root { get; private set; }

        /// <summary>
        /// 加载管理器。外部通过此属性订阅加载事件或执行加载任务。
        /// <para>例如：<c>GameLauncher.Instance.LoadingMgr.OnProgressUpdate += ...</c></para>
        /// <para>例如：<c>await GameLauncher.Instance.LoadingMgr.ExecuteAsync(tasks);</c></para>
        /// </summary>
        public LoadingManager LoadingMgr { get; private set; }

        #endregion

        #region Lifecycle Methods

        void Awake()
        {
            if (Instance != null)
            {
                Debug.LogWarning("GameLauncher: duplicate instance detected, destroying self.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Root = RootNode.Create();
            LoadingMgr = new LoadingManager();
            DontDestroyOnLoad(gameObject);
        }

        async void Start()
        {
            await LoadAndStartAsync();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (Root != null)
            {
                Root.Destroy();
                Root = null;
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
            Root.CollectLoadables(loadables);

            if (loadables.Count == 0)
            {
                // 没有需要加载的节点，直接启动
                Root.Start();
                return;
            }

            // 2. 通过 LoadingManager 统一加载
            await LoadingMgr.ExecuteLoadAsync(loadables);

            // 3. 全部加载完成，启动节点树
            Root.Start();
        }

        #endregion
    }
}
