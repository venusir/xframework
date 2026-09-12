using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// <see cref="SettingsManagerImpl{T}.Observe"/> 通知语义测试。
    /// <para>覆盖：订阅即回调当前对象、Apply 触发、退订、参数校验。</para>
    /// <para>字段级订阅（原 <c>ObserveField</c>）已整体移除——它对引用类型字段用引用相等去重，
    /// 子对象内容变化时会被静默吞掉。字段级能力改由 <see cref="SettingRef{T, TField}"/> 承担，
    /// 用例见 <c>SettingRefTests</c>。</para>
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
        public void Observe_Subscribe_ImmediatelyCallbacksCurrentObject()
        {
            var manager = CreateManager();
            var calls = new List<int>();

            var handle = manager.Observe(s => calls.Add(s.Volume));

            CollectionAssert.AreEqual(new[] { 5 }, calls,
                "订阅时立即回调当前值——与 ReactiveProperty / SettingRef 契约一致");
            handle.Dispose();
        }

        [Test]
        public void Observe_Apply_TriggersCallback()
        {
            var manager = CreateManager();
            var calls = new List<int>();
            var handle = manager.Observe(s => calls.Add(s.Volume));
            calls.Clear(); // 丢掉订阅时的立即回调，只观察 Apply 的效果

            manager.Apply(new TestSettings { Volume = 10 });

            CollectionAssert.AreEqual(new[] { 10 }, calls, "Apply 通知订阅者");
            handle.Dispose();
        }

        [Test]
        public void Observe_Dispose_StopsNotifications()
        {
            var manager = CreateManager();
            var calls = 0;
            var handle = manager.Observe(_ => calls++);

            handle.Dispose();
            var afterDispose = calls;
            manager.Apply(new TestSettings { Volume = 10 });

            Assert.AreEqual(afterDispose, calls, "退订后不再收到通知");
        }

        [Test]
        public void Observe_NullCallback_Throws()
        {
            var manager = CreateManager();
            Assert.Throws<ArgumentNullException>(() => manager.Observe(null));
        }

        #endregion
    }
}
