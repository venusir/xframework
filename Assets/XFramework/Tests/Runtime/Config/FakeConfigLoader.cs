using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using XFramework.XConfig;

namespace XFramework.XConfig.Tests
{
    /// <summary>
    /// <see cref="IConfigLoader"/> 假实现：记录加载调用计数，支持按类型注入加载任务。
    /// <para><b>与 Editor 树同名那份的关系</b>：两个测试程序集互不可见，各存一份是既有先例
    /// （<c>FakeAssetManager</c> 亦然）。差别只在用途：这一份服务 <b>PlayMode 跨帧</b>用例，
    /// 注入的任务由用例自己用 <c>UniTask.Create</c> + 逐帧 <c>Yield</c> 造出来
    /// （见 <c>ConfigPreloadFrameTests</c>）——「门控」的形态因此留在用例里，假件保持最薄。</para>
    /// </summary>
    internal sealed class FakeConfigLoader : IConfigLoader
    {
        /// <summary><see cref="LoadTableAsync{T,TKey}"/> 的调用计数。</summary>
        public int LoadTableCallCount;

        /// <summary><see cref="LoadGlobalAsync{T}"/> 的调用计数。</summary>
        public int LoadGlobalCallCount;

        /// <summary>按行类型注入的 Table 加载任务（装箱存储，取回时拆箱）。</summary>
        private readonly Dictionary<Type, object> _tableTasks = new();

        /// <summary>按配置类型注入的 Global 加载任务（装箱存储，取回时拆箱）。</summary>
        private readonly Dictionary<Type, object> _globalTasks = new();

        /// <summary>注入 Table 加载任务（按行类型）。未注入的类型返回 null 包装器。</summary>
        public void SetTableTask<T>(UniTask<ConfigTable<T>> task) where T : IConfigRow, new()
        {
            _tableTasks[typeof(T)] = task;
        }

        /// <summary>注入 Global 加载任务（按配置类型）。未注入的类型返回 null 实例。</summary>
        public void SetGlobalTask<T>(UniTask<T> task) where T : class, new()
        {
            _globalTasks[typeof(T)] = task;
        }

        public UniTask<ConfigTable<T>> LoadTableAsync<T, TKey>(string assetPath)
            where T : IConfigRow<TKey>, new()
        {
            LoadTableCallCount++;
            return _tableTasks.TryGetValue(typeof(T), out var task)
                ? (UniTask<ConfigTable<T>>)task
                : UniTask.FromResult<ConfigTable<T>>(null);
        }

        public UniTask<T> LoadGlobalAsync<T>(string assetPath)
            where T : class, new()
        {
            LoadGlobalCallCount++;
            return _globalTasks.TryGetValue(typeof(T), out var task)
                ? (UniTask<T>)task
                : UniTask.FromResult<T>(null);
        }
    }
}
