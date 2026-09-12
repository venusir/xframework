using System;
using System.Threading.Tasks;
using NUnit.Framework;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// 脏标记测试：<see cref="ISettingsManager{T}.IsDirty"/> / <see cref="ISettingsManager{T}.MarkDirty"/>。
    /// </summary>
    [TestFixture]
    public class SettingsDirtyTests
    {
        #region Test Doubles

        private sealed class SampleSettings
        {
            public int Volume;
        }

        private sealed class FakeStore : ISettingsStore
        {
            /// <summary>在写入过程中回调，用于模拟「保存期间用户继续改动设置」。</summary>
            public Action OnSave;

            public object Data;
            public bool HasData;

            public bool Exists() => HasData;

            public T Load<T>() where T : class, new() => Data as T ?? new T();

            public void Save<T>(T settings) where T : class, new()
            {
                Data = settings;
                HasData = true;
                OnSave?.Invoke();
            }

            public void Delete()
            {
                Data = null;
                HasData = false;
            }
        }

        private static SettingsManagerImpl<SampleSettings> CreateManager(ISettingsStore store)
            => new SettingsManagerImpl<SampleSettings>(store, () => new SampleSettings { Volume = 5 });

        #endregion

        #region 置脏

        [Test]
        public void NewManager_IsClean()
        {
            var manager = CreateManager(new FakeStore());

            Assert.IsFalse(manager.IsDirty, "刚构造出来时内存与持久层一致");
        }

        [Test]
        public void MarkDirty_MakesDirty()
        {
            var manager = CreateManager(new FakeStore());

            manager.MarkDirty();

            Assert.IsTrue(manager.IsDirty);
        }

        [Test]
        public void Apply_MarksDirty()
        {
            var manager = CreateManager(new FakeStore());

            manager.Apply(new SampleSettings { Volume = 9 });

            Assert.IsTrue(manager.IsDirty,
                "整体替换后内存与持久层不再一致；框架无从判断新对象是否恰好等于磁盘内容");
        }

        #endregion

        #region 清脏

        [Test]
        public void Save_ClearsDirty()
        {
            var manager = CreateManager(new FakeStore());
            manager.MarkDirty();

            manager.Save();

            Assert.IsFalse(manager.IsDirty);
        }

        [Test]
        public void Load_ClearsDirty()
        {
            var manager = CreateManager(new FakeStore());
            manager.MarkDirty();

            manager.Load();

            Assert.IsFalse(manager.IsDirty, "加载后内存即持久层内容");
        }

        [Test]
        public void Reset_ClearsDirty()
        {
            var manager = CreateManager(new FakeStore());
            manager.MarkDirty();

            manager.Reset();

            Assert.IsFalse(manager.IsDirty,
                "重置后持久层已清空、内存是默认值——重启同样得到默认值，故不算脏");
        }

        [Test]
        public async Task SaveAsync_ClearsDirty()
        {
            var manager = CreateManager(new FakeStore());
            manager.MarkDirty();

            await manager.SaveAsync();

            Assert.IsFalse(manager.IsDirty);
        }

        [Test]
        public async Task LoadAsync_ClearsDirty()
        {
            var manager = CreateManager(new FakeStore());
            manager.MarkDirty();

            await manager.LoadAsync();

            Assert.IsFalse(manager.IsDirty);
        }

        #endregion

        #region 保存期间的并发改动

        [Test]
        public void Save_ChangeDuringWrite_StaysDirty()
        {
            var store = new FakeStore();
            var manager = CreateManager(store);
            manager.MarkDirty();

            // 模拟「保存过程中用户继续拖动滑条」：写入期间又发生一次改动
            store.OnSave = () => manager.MarkDirty();

            manager.Save();

            Assert.IsTrue(manager.IsDirty,
                "保存期间的新改动不能被吞掉——这正是用变更计数而非布尔标志的原因：" +
                "快照那一刻的内容已提交，之后的改动仍未提交");
        }

        [Test]
        public void Save_ThenChange_IsDirtyAgain()
        {
            var store = new FakeStore();
            var manager = CreateManager(store);
            manager.Save();
            Assert.IsFalse(manager.IsDirty);

            manager.MarkDirty();

            Assert.IsTrue(manager.IsDirty, "保存之后的新改动应重新置脏");
        }

        #endregion

        #region 句柄写入与脏标记

        [Test]
        public void RefWrite_MarksDirty()
        {
            SettingsManager.Destroy();
            SettingsManager.Initialize<SampleSettings>(new FakeStore(), () => new SampleSettings { Volume = 5 });
            try
            {
                var volume = SettingsManager.Ref<SampleSettings, int>(s => s.Volume);

                volume.Value = 9;

                Assert.IsTrue(SettingsManager.IsDirty<SampleSettings>(), "经句柄写入应自动置脏");
            }
            finally
            {
                SettingsManager.Destroy();
            }
        }

        [Test]
        public void RefWriteSameValue_DoesNotMarkDirty()
        {
            SettingsManager.Destroy();
            SettingsManager.Initialize<SampleSettings>(new FakeStore(), () => new SampleSettings { Volume = 5 });
            try
            {
                var volume = SettingsManager.Ref<SampleSettings, int>(s => s.Volume);

                volume.Value = 5; // 与当前值相同，被去重拦下

                Assert.IsFalse(SettingsManager.IsDirty<SampleSettings>(),
                    "写入相同值没有产生改动，不应置脏——否则拖动滑条回到原值会误触发自动保存");
            }
            finally
            {
                SettingsManager.Destroy();
            }
        }

        #endregion
    }
}
