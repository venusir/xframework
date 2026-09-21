using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// 异步持久化 API 测试：<c>SaveAsync</c> / <c>LoadAsync</c> 在「store 实现
    /// <see cref="IAsyncSettingsStore"/>」与「store 仅实现同步 <see cref="ISettingsStore"/>」
    /// 两条路径下的行为，以及释放与取消语义。
    /// </summary>
    /// <remarks>
    /// 这些是 PlayMode 测试，PlayerLoop 在跑，故 <c>SwitchToMainThread</c> 能正常完成——
    /// 「用例没有挂起」本身就是线程约定的旁证（EditMode 下会死等）。
    /// </remarks>
    [TestFixture]
    public class SettingsAsyncTests
    {
        #region Test Doubles

        private sealed class SampleSettings
        {
            public int Volume;
        }

        /// <summary>仅同步能力的存储：管理器应走线程池降级路径，且不得调用它的同步成员以外的任何东西。</summary>
        private sealed class SyncOnlyStore : ISettingsStore
        {
            public object Data;
            public bool HasData;
            public int SyncSaveCalls;

            /// <summary>可在 <see cref="Exists"/> 里执行的一次性副作用（用于「读取中换后端」）。</summary>
            public Action OnExists;

            public bool Exists()
            {
                var hook = OnExists;
                OnExists = null; // 一次性：避免后续调用重复触发
                hook?.Invoke();

                return HasData;
            }

            public T Load<T>() where T : class, new() => Data as T ?? new T();

            public void Save<T>(T settings) where T : class, new()
            {
                SyncSaveCalls++;
                Data = settings;
                HasData = true;
            }

            public void Delete()
            {
                Data = null;
                HasData = false;
            }
        }

        /// <summary>实现异步能力接口的存储：管理器应直接调用其异步成员，不碰同步成员。</summary>
        private sealed class AsyncStore : IAsyncSettingsStore
        {
            public object Data;
            public bool HasData;
            public int AsyncLoadCalls;
            public int AsyncSaveCalls;
            public int SyncMemberCalls;

            /// <summary>令 <see cref="LoadAsync{T}"/> 返回 <c>null</c>，验证异步路径同样有 null 防御。</summary>
            public bool ReturnNullOnLoad;

            public bool Exists() => HasData;

            public T Load<T>() where T : class, new()
            {
                SyncMemberCalls++;
                return new T();
            }

            public void Save<T>(T settings) where T : class, new() => SyncMemberCalls++;

            public void Delete() { }

            public UniTask<bool> ExistsAsync(CancellationToken cancellationToken = default)
                => UniTask.FromResult(HasData);

            public UniTask<T> LoadAsync<T>(CancellationToken cancellationToken = default) where T : class, new()
            {
                AsyncLoadCalls++;
                return UniTask.FromResult(ReturnNullOnLoad ? null : Data as T ?? new T());
            }

            public UniTask SaveAsync<T>(T settings, CancellationToken cancellationToken = default) where T : class, new()
            {
                AsyncSaveCalls++;
                Data = settings;
                HasData = true;
                return UniTask.CompletedTask;
            }
        }

        private static SettingsManagerImpl<SampleSettings> CreateManager(ISettingsStore store)
            => new SettingsManagerImpl<SampleSettings>(store, () => new SampleSettings { Volume = 5 });

        #endregion

        #region SaveAsync

        [Test]
        public async Task SaveAsync_AsyncStore_UsesStoreAsyncMember()
        {
            var store = new AsyncStore();
            var manager = CreateManager(store);
            manager.Settings.Volume = 42;

            await manager.SaveAsync();

            Assert.AreEqual(1, store.AsyncSaveCalls, "实现 IAsyncSettingsStore 时应直接调用其异步成员");
            Assert.AreEqual(0, store.SyncMemberCalls, "异步路径不得回落到同步成员");
            Assert.AreEqual(42, ((SampleSettings)store.Data).Volume);
        }

        [Test]
        public async Task SaveAsync_SyncOnlyStore_FallsBackToThreadPool()
        {
            var store = new SyncOnlyStore();
            var manager = CreateManager(store);
            manager.Settings.Volume = 42;

            await manager.SaveAsync();

            Assert.AreEqual(1, store.SyncSaveCalls, "仅同步的 store 经线程池降级后完成保存");
            Assert.AreEqual(42, ((SampleSettings)store.Data).Volume);
        }

        [Test]
        public void SaveAsync_AfterDispose_ThrowsSynchronously()
        {
            var manager = CreateManager(new SyncOnlyStore());
            manager.Dispose();

            // 释放检查在同步段，故这里同步抛而非包进返回的 UniTask——
            // 若写进 async 方法体，调用方拿不到同步失败（与 SavePathUtility 的设计理由一致）
            Assert.Throws<ObjectDisposedException>(() => manager.SaveAsync());
        }

        #endregion

        #region LoadAsync

        [Test]
        public async Task LoadAsync_AsyncStore_UsesStoreAsyncMember()
        {
            var store = new AsyncStore { HasData = true, Data = new SampleSettings { Volume = 99 } };
            var manager = CreateManager(store);

            // 构造函数必然走同步路径（它无法 await），有数据时会调用一次同步 Load。
            // 这里清零，让断言只反映 LoadAsync 自身的行为
            store.SyncMemberCalls = 0;

            await manager.LoadAsync();

            Assert.AreEqual(1, store.AsyncLoadCalls);
            Assert.AreEqual(0, store.SyncMemberCalls, "异步路径不得回落到同步成员");
            Assert.AreEqual(99, manager.Settings.Volume);
        }

        [Test]
        public async Task LoadAsync_AsyncStore_NoData_UsesDefaultFactory()
        {
            var store = new AsyncStore { HasData = false };
            var manager = CreateManager(store);
            manager.Settings.Volume = 42;

            await manager.LoadAsync();

            Assert.AreEqual(5, manager.Settings.Volume,
                "异步路径同样要区分「无数据」与「读到数据」——否则 defaultFactory 形同虚设");
        }

        [Test]
        public async Task LoadAsync_AsyncStore_ReturnsNull_FallsBackToDefault()
        {
            var store = new AsyncStore { HasData = true, ReturnNullOnLoad = true };
            var manager = CreateManager(store);

            LogAssert.Expect(LogType.Warning, new Regex(@"ISettingsStore\.Load<SampleSettings> 返回了 null"));
            await manager.LoadAsync();

            Assert.IsNotNull(manager.Settings, "不得把 null 交给调用方");
            Assert.AreEqual(5, manager.Settings.Volume, "回退 defaultFactory");
        }

        [Test]
        public async Task LoadAsync_SyncOnlyStore_FallsBackToThreadPool()
        {
            var store = new SyncOnlyStore { HasData = true, Data = new SampleSettings { Volume = 99 } };
            var manager = CreateManager(store);

            await manager.LoadAsync();

            Assert.AreEqual(99, manager.Settings.Volume);
        }

        [Test]
        public async Task LoadAsync_SyncOnlyStore_NoData_UsesDefaultFactory()
        {
            var store = new SyncOnlyStore { HasData = false };
            var manager = CreateManager(store);
            manager.Settings.Volume = 42;

            await manager.LoadAsync();

            Assert.AreEqual(5, manager.Settings.Volume, "降级路径同样走 defaultFactory");
        }

        [Test]
        public async Task LoadAsync_SyncOnlyStoreSwappedMidLoad_UsesCapturedStore()
        {
            var original = new SyncOnlyStore { HasData = true, Data = new SampleSettings { Volume = 111 } };
            var manager = CreateManager(original);
            var replacement = new SyncOnlyStore { HasData = true, Data = new SampleSettings { Volume = 222 } };

            // 降级路径把整段同步读挪到线程池，期间换后端正是「能力探测与真正读取分家」
            // 最容易发生的位置——探测看到的是一个后端，读却落到另一个上
            original.OnExists = () => manager.Store = replacement;

            LogAssert.Expect(LogType.Warning, new Regex(@"Store 已替换为 SyncOnlyStore"));
            await manager.LoadAsync();

            Assert.AreEqual(111, manager.Settings.Volume,
                "一次加载只用开头捕获的后端，与同步 Load 的语义一致");

            manager.Dispose();
        }

        [Test]
        public async Task LoadAsync_NotifiesSubscribers()
        {
            var manager = CreateManager(new SyncOnlyStore { HasData = true, Data = new SampleSettings { Volume = 9 } });
            var notified = 0;
            using var handle = manager.Observe(_ => notified++);
            var before = notified; // Observe 订阅时已立即回调过当前对象，这里先记基线再观察 LoadAsync 的那一次

            await manager.LoadAsync();

            Assert.AreEqual(before + 1, notified, "LoadAsync 完成后通知订阅者一次");
        }

        [Test]
        public void LoadAsync_AfterDispose_ThrowsSynchronously()
        {
            var manager = CreateManager(new SyncOnlyStore());
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => manager.LoadAsync());
        }

        [Test]
        public async Task LoadAsync_Cancelled_Throws()
        {
            var manager = CreateManager(new SyncOnlyStore { HasData = true, Data = new SampleSettings { Volume = 9 } });

            // 取消必须向外传播而不是被吞掉
            bool cancelled = false;
            try
            {
                await manager.LoadAsync(new CancellationToken(canceled: true));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.IsTrue(cancelled, "已取消的令牌应导致 OperationCanceledException");
        }

        #endregion
    }
}
