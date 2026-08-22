using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="IConfigLoader"/> 假实现：记录加载调用计数，支持按类型注入 TCS 门控任务模拟并发挂起。
    /// <para>供 <see cref="ConfigManagerImpl"/> 并发共享与校验顺序测试断言，不依赖真实资源。</para>
    /// </summary>
    internal class FakeConfigLoader : IConfigLoader
    {
        /// <summary>LoadTableAsync 调用计数。</summary>
        public int LoadTableCallCount;

        /// <summary>LoadGlobalAsync 调用计数。</summary>
        public int LoadGlobalCallCount;

        /// <summary>按行类型注入的 Table 加载任务（装箱存储，取回时拆箱）。</summary>
        private readonly Dictionary<Type, object> _tableTasks = new();

        /// <summary>按类型注入的 Global 加载任务（装箱存储，取回时拆箱）。</summary>
        private readonly Dictionary<Type, object> _globalTasks = new();

        /// <summary>注入 Table 加载任务（按行类型）。未注入的类型加载返回 null 包装器。</summary>
        public void SetTableTask<T>(UniTask<ConfigTable<T>> task) where T : IConfigRow, new()
        {
            _tableTasks[typeof(T)] = task;
        }

        /// <summary>注入 Global 加载任务（按配置类型）。未注入的类型加载返回 null 实例。</summary>
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
