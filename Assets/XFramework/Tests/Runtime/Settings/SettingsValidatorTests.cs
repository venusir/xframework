using System;
using System.Threading.Tasks;
using NUnit.Framework;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// <see cref="ISettingsValidator{T}"/> 载荷校验钩子测试。
    /// <para>覆盖：加载时钳制越界值、迁移之后执行、<c>Reset</c> 与 <c>Apply</c> 与异步加载三条路径、
    /// 经门面的注册，以及两条<b>刻意的边界</b>（构造期那次加载、经句柄的字段写入）。</para>
    /// </summary>
    [TestFixture]
    public class SettingsValidatorTests
    {
        #region Test Doubles

        [Serializable]
        private sealed class SampleSettings
        {
            public int Volume;
        }

        private sealed class SeededStore : ISettingsStore
        {
            public object Payload;

            public bool Exists() => Payload != null;

            public T Load<T>() where T : class, new() => Payload as T ?? new T();

            public void Save<T>(T settings) where T : class, new() => Payload = settings;

            public void Delete() => Payload = null;
        }

        /// <summary>把 Volume 钳到 [0, 100]——「不可信的值不能进内存」的典型实现。</summary>
        private sealed class ClampingValidator : ISettingsValidator<SampleSettings>
        {
            public int Calls;

            public void Validate(SampleSettings settings)
            {
                Calls++;
                if (settings.Volume < 0)
                    settings.Volume = 0;
                else if (settings.Volume > 100)
                    settings.Volume = 100;
            }
        }

        /// <summary>模拟「迁移实现写错了字段」——它产出的值同样要过校验。</summary>
        private sealed class BadMigrator : ISettingsMigrator<SampleSettings>
        {
            public void Migrate(int fromVersion, int toVersion, SampleSettings settings)
            {
                settings.Volume = 9999;
            }
        }

        private static SettingsManagerImpl<SampleSettings> CreateManager(
            ISettingsStore store, SettingsOptions options = null)
            => new SettingsManagerImpl<SampleSettings>(store, () => new SampleSettings { Volume = 5 }, options);

        #endregion

        #region Fixture

        [SetUp]
        public void SetUp() => SettingsManager.Destroy();

        [TearDown]
        public void TearDown() => SettingsManager.Destroy();

        #endregion

        #region 加载

        [Test]
        public void Load_ClampsOutOfRangeValueFromStore()
        {
            var store = new SeededStore();
            var manager = CreateManager(store);
            manager.Validator = new ClampingValidator();

            store.Payload = new SampleSettings { Volume = 9999 }; // 手改过的 JSON / 位翻转
            manager.Load();

            Assert.AreEqual(100, manager.Settings.Volume, "越界值在进入内存前被钳制");
        }

        [Test]
        public void SetValidator_ThenLoad_AlsoCoversStartupData()
        {
            // 构造期那次加载发生在钩子注册之前（属性只能在拿到实例后设），故那份数据未经校验。
            // 本用例钉住文档给出的补救方式：注册后补一次 Load
            var store = new SeededStore { Payload = new SampleSettings { Volume = 9999 } };

            SettingsManager.Initialize<SampleSettings>(store, () => new SampleSettings());
            Assert.AreEqual(9999, SettingsManager.Settings<SampleSettings>().Volume,
                "前提：构造期没有钩子可注册，故那份数据原样进内存");

            SettingsManager.SetValidator<SampleSettings>(new ClampingValidator());
            SettingsManager.Load<SampleSettings>();

            Assert.AreEqual(100, SettingsManager.Settings<SampleSettings>().Volume,
                "注册后补一次 Load 即可让校验器作用于启动时读到的那份数据");
        }

        [Test]
        public void Load_ValidatesAfterMigration()
        {
            var store = new SeededStore();
            var manager = CreateManager(store, new SettingsOptions { CurrentVersion = 2 });
            manager.Migrator = new BadMigrator();
            manager.Validator = new ClampingValidator();

            store.Payload = new SettingsEnvelope<SampleSettings>
            {
                Version = 1,
                Data = new SampleSettings { Volume = 7 },
            };
            manager.Load();

            Assert.AreEqual(100, manager.Settings.Volume,
                "迁移的产出同样过校验——顺序是先迁移、后校验");
        }

        #endregion

        #region 其余产出路径

        [Test]
        public void Reset_ValidatesFactoryDefault()
        {
            // 工厂产出越界默认值：Reset 换掉实例后同样要过校验
            var manager = new SettingsManagerImpl<SampleSettings>(
                new SeededStore(), () => new SampleSettings { Volume = -50 });
            manager.Validator = new ClampingValidator();

            manager.Reset();

            Assert.AreEqual(0, manager.Settings.Volume, "重置产出的越界默认值被钳制");
        }

        [Test]
        public void Apply_ValidatesCallerSuppliedObject()
        {
            var manager = CreateManager(new SeededStore());
            manager.Validator = new ClampingValidator();

            manager.Apply(new SampleSettings { Volume = 500 });

            Assert.AreEqual(100, manager.Settings.Volume,
                "整体替换也是产出路径：校验之后才成为当前对象");
        }

        [Test]
        public async Task LoadAsync_Validates()
        {
            var store = new SeededStore { Payload = new SampleSettings { Volume = 9999 } };
            var manager = CreateManager(store);
            manager.Validator = new ClampingValidator();

            await manager.LoadAsync();

            Assert.AreEqual(100, manager.Settings.Volume, "异步加载与同步加载同一条产出路径");
        }

        #endregion

        #region 两条刻意的边界

        [Test]
        public void SettingRefWrite_IsNotValidated()
        {
            SettingsManager.Initialize<SampleSettings>(new SeededStore());
            SettingsManager.SetValidator<SampleSettings>(new ClampingValidator());
            var volume = SettingsManager.Ref<SampleSettings, int>(s => s.Volume);

            volume.Value = 9999;

            Assert.AreEqual(9999, SettingsManager.Settings<SampleSettings>().Volume,
                "经句柄写入是进程内的显式赋值，不经校验——这是刻意的边界而非遗漏。" +
                "需要逐次拦截请在写入侧自己做");
        }

        #endregion

        #region 门面

        [Test]
        public void Facade_SetValidator_WiresThrough()
        {
            SettingsManager.Initialize<SampleSettings>(new SeededStore());
            var validator = new ClampingValidator();

            SettingsManager.SetValidator<SampleSettings>(validator);

            Assert.AreSame(validator, SettingsManager.GetValidator<SampleSettings>());
        }

        #endregion
    }
}
