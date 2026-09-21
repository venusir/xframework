using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// <see cref="SettingsManagerImpl{T}"/> 的存储后端替换语义与释放语义。
    /// <para>覆盖 D5（替换后端只换引用、不迁移内存数据）与 D10（释放后不再静默写盘、
    /// 不再返回永不回调的空订阅句柄）。</para>
    /// </summary>
    [TestFixture]
    public class SettingsManagerLifecycleTests
    {
        #region Test Doubles

        private sealed class SampleSettings
        {
            public int Volume;
        }

        private sealed class FakeStore : ISettingsStore
        {
            public object Data;

            public bool Exists() => false;

            public T Load<T>() where T : class, new() => new T();

            public void Save<T>(T settings) where T : class, new() => Data = settings;

            public void Delete() { }
        }

        /// <summary>
        /// 可在 <see cref="Exists"/> 里执行一次性副作用的假存储。
        /// 用于制造「读取过程中后端被换掉」——真实场景少见，但门面不设防，
        /// 而代价不对称：不快照就会读到另一个后端的数据且毫无提示。
        /// </summary>
        private sealed class HookedStore : ISettingsStore
        {
            public object Payload;
            public Action OnExists;

            public bool Exists()
            {
                var hook = OnExists;
                OnExists = null; // 一次性：避免后续调用重复触发
                hook?.Invoke();

                return Payload != null;
            }

            public T Load<T>() where T : class, new() => Payload as T ?? new T();

            public void Save<T>(T settings) where T : class, new() => Payload = settings;

            public void Delete() => Payload = null;
        }

        private static SettingsManagerImpl<SampleSettings> CreateManager(ISettingsStore store)
            => new SettingsManagerImpl<SampleSettings>(store, () => new SampleSettings { Volume = 5 });

        #endregion

        #region 存储后端替换

        [Test]
        public void Store_Replace_WarnsAndKeepsInMemoryData()
        {
            var newStore = new FakeStore();
            var manager = CreateManager(new FakeStore());

            LogAssert.Expect(LogType.Warning, new Regex(@"Store 已替换为 FakeStore"));
            manager.Store = newStore;

            Assert.AreSame(newStore, manager.Store);
            Assert.AreEqual(5, manager.Settings.Volume, "替换后端不迁移数据，内存中的设置保持不变");
        }

        [Test]
        public void Store_Replace_ThenSave_WritesToNewStore()
        {
            var newStore = new FakeStore();
            var manager = CreateManager(new FakeStore());

            LogAssert.Expect(LogType.Warning, new Regex(@"Store 已替换为 FakeStore"));
            manager.Store = newStore;

            manager.Settings.Volume = 42;
            manager.Save();

            Assert.AreSame(manager.Settings, newStore.Data, "下一次 Save 写入的是新后端");
        }

        [Test]
        public void Store_SetNull_Throws()
        {
            var manager = CreateManager(new FakeStore());

            Assert.Throws<ArgumentNullException>(() => manager.Store = null);
        }

        [Test]
        public void Load_StoreSwappedDuringRead_UsesTheStoreCapturedAtEntry()
        {
            var original = new HookedStore { Payload = new SampleSettings { Volume = 111 } };
            var manager = CreateManager(original);
            var replacement = new HookedStore { Payload = new SampleSettings { Volume = 222 } };

            // 在「先问有没有数据」这一步把后端换掉。不加干预的话，接下来的 Load 会去读新后端
            original.OnExists = () => manager.Store = replacement;

            LogAssert.Expect(LogType.Warning, new Regex(@"Store 已替换为 HookedStore"));
            manager.Load();

            Assert.AreEqual(111, manager.Settings.Volume,
                "一次加载只用开头捕获的后端：Exists 与 Load 不能落在两个不同的后端上");
        }

        #endregion

        #region 释放语义

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            var manager = CreateManager(new FakeStore());
            manager.Dispose();

            Assert.DoesNotThrow(() => manager.Dispose());
        }

        [Test]
        public void Dispose_ThenSave_Throws()
        {
            var store = new FakeStore();
            var manager = CreateManager(store);
            manager.Dispose();

            // 修复前这里照常写盘：已释放的管理器仍在修改持久层
            Assert.Throws<ObjectDisposedException>(() => manager.Save());
            Assert.IsNull(store.Data, "已释放的管理器不得再写盘");
        }

        [Test]
        public void Dispose_ThenAccessSettings_Throws()
        {
            var manager = CreateManager(new FakeStore());
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => { _ = manager.Settings; });
        }

        [Test]
        public void Dispose_ThenMutatingMembers_Throw()
        {
            var manager = CreateManager(new FakeStore());
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => manager.Load());
            Assert.Throws<ObjectDisposedException>(() => manager.Reset());
            Assert.Throws<ObjectDisposedException>(() => manager.Apply(new SampleSettings()));
        }

        [Test]
        public void Dispose_ThenObserve_Throws()
        {
            var manager = CreateManager(new FakeStore());
            manager.Dispose();

            // 修复前这里返回一个永不回调的空句柄，订阅方毫无察觉
            Assert.Throws<ObjectDisposedException>(() => manager.Observe(_ => { }));
        }

        #endregion
    }
}
