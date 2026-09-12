using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// SettingsManagerImpl 响应式通知测试。
    /// <para>覆盖 Observe/ObserveField 语义:触发、首次必过、去重、字段隔离、退订。</para>
    /// </summary>
    [TestFixture]
    public class SettingsManagerImplTests
    {
        #region Private

        private sealed class TestSettings
        {
            public int Volume;
            public string Name;
        }

        /// <summary>内存假存储:Exists 恒 false(构造走默认值路径),Load/Save 无操作。</summary>
        private sealed class MemoryStore : ISettingsStore
        {
            public bool Exists() => false;
            public T Load<T>() where T : class, new() => new T();
            public void Save<T>(T settings) where T : class, new() { }
            public void Delete() { }
        }

        private static SettingsManagerImpl<TestSettings> CreateManager()
            => new SettingsManagerImpl<TestSettings>(new MemoryStore(), () => new TestSettings { Volume = 5, Name = "init" });

        #endregion

        #region Observe

        [Test]
        public void Observe_Apply_TriggersCallback()
        {
            var manager = CreateManager();
            var calls = new List<int>();
            var handle = manager.Observe(s => calls.Add(s.Volume));

            manager.Apply(new TestSettings { Volume = 10 });

            CollectionAssert.AreEqual(new[] { 10 }, calls, "Apply 通知订阅者");
            handle.Dispose();
        }

        [Test]
        public void Observe_DoesNotCallbackOnSubscribe()
        {
            var manager = CreateManager();
            var calls = 0;
            var handle = manager.Observe(_ => calls++);

            Assert.AreEqual(0, calls, "订阅时不立即回调(Observe 无缓冲语义)");
            handle.Dispose();
        }

        [Test]
        public void Observe_Dispose_StopsNotifications()
        {
            var manager = CreateManager();
            var calls = 0;
            var handle = manager.Observe(_ => calls++);

            handle.Dispose();
            manager.Apply(new TestSettings { Volume = 10 });

            Assert.AreEqual(0, calls, "退订后不再收到通知");
        }

        [Test]
        public void Observe_NullCallback_Throws()
        {
            var manager = CreateManager();
            Assert.Throws<ArgumentNullException>(() => manager.Observe(null));
        }

        #endregion

        #region ObserveField

        [Test]
        public void ObserveField_FirstValue_AlwaysPasses()
        {
            var manager = CreateManager();
            var calls = new List<int>();
            var handle = manager.ObserveField(s => s.Volume, calls.Add);

            // 首次 Apply(即使字段值恰好等于初始字段值)必过:观察者无"初始缓存"概念
            manager.Apply(new TestSettings { Volume = 5 });

            CollectionAssert.AreEqual(new[] { 5 }, calls, "首次值必过(无缓存时不去重)");
            handle.Dispose();
        }

        [Test]
        public void ObserveField_SameValue_Dedup()
        {
            var manager = CreateManager();
            var calls = new List<int>();
            var handle = manager.ObserveField(s => s.Volume, calls.Add);

            manager.Apply(new TestSettings { Volume = 5 });
            manager.Apply(new TestSettings { Volume = 5 });
            manager.Apply(new TestSettings { Volume = 6 });

            CollectionAssert.AreEqual(new[] { 5, 6 }, calls, "相同字段值去重,新值必过");
            handle.Dispose();
        }

        [Test]
        public void ObserveField_OtherFieldChanged_FirstNotifyStillFires()
        {
            var manager = CreateManager();
            var volumeCalls = new List<int>();
            var nameCalls = new List<string>();
            var h1 = manager.ObserveField(s => s.Volume, volumeCalls.Add);
            var h2 = manager.ObserveField(s => s.Name, nameCalls.Add);

            // 只改 Volume:Volume 观察者收到新值
            manager.Apply(new TestSettings { Volume = 99, Name = "init" });

            CollectionAssert.AreEqual(new[] { 99 }, volumeCalls);

            // Name 观察者也回调一次,但这不是「字段隔离失效」而是「首次必过」优先:
            // 这是它第一次收到通知,去重尚未建立基线,故即使它选中的 Name 未变也会回调。
            // 真正「只在本字段变化时回调」需要字段身份,由阶段三的 SettingRef 提供。
            CollectionAssert.AreEqual(new[] { "init" }, nameCalls, "首次必过优先于字段隔离");

            // 基线既已建立,再次 Apply(仍改 Volume、Name 未变)时 Name 观察者不再回调
            manager.Apply(new TestSettings { Volume = 100, Name = "init" });

            CollectionAssert.AreEqual(new[] { 99, 100 }, volumeCalls);
            CollectionAssert.AreEqual(new[] { "init" }, nameCalls, "去重基线建立后,选中值未变则不再回调");

            h1.Dispose();
            h2.Dispose();
        }

        [Test]
        public void ObserveField_NullSelector_Throws()
        {
            var manager = CreateManager();
            Assert.Throws<ArgumentNullException>(() => manager.ObserveField<int>(null, _ => { }));
        }

        #endregion
    }
}
