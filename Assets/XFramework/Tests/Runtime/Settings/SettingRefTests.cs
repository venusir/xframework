using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XSettings;
using XFramework.XUI.Data;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// <see cref="SettingRef{T, TField}"/> 字段句柄测试。
    /// <para>覆盖：表达式校验、写穿到 POCO、去重、订阅即回调、<b>实例替换后自动跟随</b>、
    /// 直改字段不通知、以及句柄可直接接入 UI 绑定。</para>
    /// </summary>
    [TestFixture]
    public class SettingRefTests
    {
        #region Test Doubles

        [Serializable]
        private sealed class AudioSettings
        {
            public float MasterVolume = 1f;
        }

        [Serializable]
        private sealed class GameSettings
        {
            public AudioSettings Audio = new();
            public string Name = "default";

            /// <summary>属性——用于验证「设置项不能用属性」的拒绝路径。</summary>
            public float VolumeAsProperty
            {
                get => Audio.MasterVolume;
                set => Audio.MasterVolume = value;
            }

            /// <summary>方法——用于验证不支持方法调用的拒绝路径。</summary>
            public float GetVolume() => Audio.MasterVolume;
        }

        private sealed class MemoryStore : ISettingsStore
        {
            public object Data;
            public bool HasData;

            public bool Exists() => HasData;

            public T Load<T>() where T : class, new() => Data as T ?? new T();

            public void Save<T>(T settings) where T : class, new()
            {
                Data = settings;
                HasData = true;
            }

            public void Delete()
            {
                Data = null;
                HasData = false;
            }
        }

        #endregion

        #region Fixture

        [SetUp]
        public void SetUp() => SettingsManager.Destroy();

        [TearDown]
        public void TearDown() => SettingsManager.Destroy();

        /// <summary>初始化一个干净的 GameSettings（无持久化数据，走 defaultFactory）。</summary>
        private static MemoryStore Init(MemoryStore store = null)
        {
            store ??= new MemoryStore();
            SettingsManager.Initialize<GameSettings>(store, () => new GameSettings());
            return store;
        }

        #endregion

        #region 表达式校验

        [Test]
        public void Ref_PropertyLeaf_ThrowsWithReason()
        {
            var ex = Assert.Throws<ArgumentException>(
                () => SettingsManager.Ref<GameSettings, float>(s => s.VolumeAsProperty));

            StringAssert.Contains("字段", ex.Message);
            StringAssert.Contains("JsonUtility", ex.Message,
                "报错要说明理由：JsonUtility 只序列化字段，用属性会「改了但没存」");
        }

        [Test]
        public void Ref_MethodCall_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => SettingsManager.Ref<GameSettings, float>(s => s.GetVolume()));
        }

        #endregion

        #region 读写

        [Test]
        public void Ref_Write_UpdatesPocoAndNotifies()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var received = new List<float>();
            using var handle = volume.Subscribe(received.Add);

            volume.Value = 0.5f;

            Assert.AreEqual(0.5f, SettingsManager.Settings<GameSettings>().Audio.MasterVolume, 1e-5f,
                "写入要落回 POCO 字段");
            CollectionAssert.AreEqual(new[] { 1f, 0.5f }, received, "订阅即回调当前值，写入后再回调新值");
        }

        [Test]
        public void Ref_WriteSameValue_DoesNotNotify()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var count = 0;
            using var handle = volume.Subscribe(_ => count++);
            Assert.AreEqual(1, count, "订阅即回调一次");

            volume.Value = 1f; // 与当前值相同

            Assert.AreEqual(1, count, "相同值不通知");
        }

        [Test]
        public void Ref_ReadsLiveValueThroughNestedPath()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);

            SettingsManager.Settings<GameSettings>().Audio.MasterVolume = 0.25f; // 直改字段

            Assert.AreEqual(0.25f, volume.Value, 1e-5f,
                "句柄每次访问都穿透当前实例，读到实时值——去重基准不缓存，故无陈旧锚点");
        }

        [Test]
        public void Ref_DirectPocoEdit_DoesNotNotify()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var count = 0;
            using var handle = volume.Subscribe(_ => count++);

            SettingsManager.Settings<GameSettings>().Audio.MasterVolume = 0.25f; // 直改字段

            Assert.AreEqual(1, count,
                "直改 POCO 不通知——这是保留的已知限制，要通知必须经句柄写入");
        }

        #endregion

        #region 实例替换后自动跟随

        [Test]
        public void Ref_AfterLoad_FollowsReplacedInstance()
        {
            var store = Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var before = SettingsManager.Settings<GameSettings>();

            store.Data = new GameSettings { Audio = new AudioSettings { MasterVolume = 0.8f } };
            store.HasData = true;
            SettingsManager.Load<GameSettings>();

            var after = SettingsManager.Settings<GameSettings>();
            Assert.AreNotSame(before, after, "前提：Load 确实换掉了实例");
            Assert.AreEqual(0.8f, volume.Value, 1e-5f, "句柄自动跟随新实例，无需重新绑定");
        }

        [Test]
        public void Ref_AfterReset_ReadsFactoryDefault()
        {
            var store = new MemoryStore
            {
                Data = new GameSettings { Audio = new AudioSettings { MasterVolume = 0.8f } },
                HasData = true,
            };
            SettingsManager.Initialize<GameSettings>(store,
                () => new GameSettings { Audio = new AudioSettings { MasterVolume = 0.3f } });
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            Assert.AreEqual(0.8f, volume.Value, 1e-5f);

            SettingsManager.Reset<GameSettings>();

            Assert.AreEqual(0.3f, volume.Value, 1e-5f, "Reset 换实例后句柄读到 defaultFactory 的产出");
        }

        [Test]
        public void Ref_SurvivesDestroyAndReinitialize()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);

            SettingsManager.Destroy();
            SettingsManager.Initialize<GameSettings>(new MemoryStore(), () => new GameSettings());

            Assert.AreEqual(1f, volume.Value, 1e-5f, "句柄是常驻对象，不随管理器销毁而失效");
        }

        #endregion

        #region 生命周期

        [Test]
        public void Ref_CreatedBeforeInitialize_WorksAfterInitialize()
        {
            SettingsManager.Destroy();

            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);

            Init();

            Assert.AreEqual(1f, volume.Value, 1e-5f,
                "解析表达式不需要实例，句柄在真正读写时才去找当前实例");
        }

        [Test]
        public void Ref_AccessBeforeInitialize_ThrowsRepairHint()
        {
            SettingsManager.Destroy();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);

            var ex = Assert.Throws<InvalidOperationException>(() => { _ = volume.Value; });

            StringAssert.Contains("Initialize", ex.Message, "未初始化访问要带修复提示");
        }

        [Test]
        public void Ref_DisposedSubscription_StopsNotifications()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var count = 0;
            var handle = volume.Subscribe(_ => count++);

            handle.Dispose();
            volume.Value = 0.5f;

            Assert.AreEqual(1, count, "退订后不再收到通知");
        }

        #endregion

        #region 与 UI 模块的衔接

        [Test]
        public void Ref_BindsThroughUIBinder()
        {
            // 这一例是方案丙的目的所在：句柄实现 IReactiveProperty<T>，故 UI 模块现成的绑定
            // 扩展方法可直接接收。若 UIBinder 的接收者被改回具体类型，本用例会先编译失败
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var received = new List<float>();

            using var handle = volume.Bind(received.Add);

            volume.Value = 0.5f;

            CollectionAssert.AreEqual(new[] { 1f, 0.5f }, received);
        }

        #endregion
    }
}
