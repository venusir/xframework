using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 游戏启动器。作为 Unity 与节点树之间的生命周期桥接。
    /// <para>挂载在场景中的 GameObject 上，负责创建和管理 <see cref="RootNode"/> 的生命周期。</para>
    /// <para>启动时自动收集所有 <see cref="ILoadable"/> 子节点，等待全部加载完成后才调用 <see cref="BaseNode.Start"/>。</para>
    /// </summary>
    public class GameLauncher : MonoBehaviour
    {
        /// <summary>
        /// 加载进度变更事件。参数为 (整体进度 0~1, 当前描述文字)。
        /// <para>外部 UI 可订阅此事件以显示加载进度条。</para>
        /// </summary>
        public event Action<float, string> OnLoadingProgress;

        /// <summary>
        /// 当前节点树的根节点。
        /// </summary>
        public RootNode Root { get; private set; }

        void Awake()
        {
            Root = RootNode.Create();
            DontDestroyOnLoad(gameObject);
        }

        async void Start()
        {
            await LoadAndStartAsync();
        }

        void OnDestroy()
        {
            if (Root != null)
            {
                Root.Destroy();
                Root = null;
            }
        }

        /// <summary>
        /// 加载并启动异步流程。
        /// <para>流程：收集所有 ILoadable → 同时启动加载 → 每帧轮询进度 → 全部完成 → 调用 Root.Start()</para>
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

            // 2. 同时启动所有加载任务
            UniTask[] tasks = new UniTask[loadables.Count];
            for (int i = 0; i < loadables.Count; i++)
            {
                tasks[i] = loadables[i].LoadAsync();
            }

            // 3. 每帧轮询进度，直到全部加载完成
            while (true)
            {
                float totalProgress = 0f;
                string currentDesc = null;
                bool allDone = true;

                for (int i = 0; i < loadables.Count; i++)
                {
                    float p = loadables[i].Progress;
                    totalProgress += p;

                    if (p < 1f)
                    {
                        allDone = false;
                        currentDesc = loadables[i].Description;
                    }
                }

                float overallProgress = totalProgress / loadables.Count;
                OnLoadingProgress?.Invoke(overallProgress, currentDesc ?? "加载完成");

                if (allDone)
                    break;

                // 等待下一帧
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            // 4. 等待所有加载任务完成（确保所有协程已结束）
            await UniTask.WhenAll(tasks);

            // 5. 全部加载完成，启动节点树
            Root.Start();
        }
    }
}
