using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;
using XFramework.XUI.Data;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// <see cref="SettingRef{T, TField}"/> 字段句柄测试。
    /// <para>覆盖：表达式校验、写穿到 POCO、去重、订阅即回调、<b>实例替换后自动跟随与重放</b>、
    /// 直改字段不通知、订阅立即回调抛异常时订阅必须被清理、TryWriteValue 在类型不可用时返回 false 且不抛、
    /// 以及句柄可直接接入 UI 绑定。</para>
    /// <remarks>
    /// <b>两条纪律（重放注册表是进程级的，跨用例共享）：</b>
    /// <list type="number">
    /// <item>各用例专用的设置类型保持私有嵌套，避免与他处共用同一张句柄表；</item>
    /// <item>不得留下未释放的活订阅——句柄本身无生命周期，但订阅句柄必须 <c>using</c> 掉，
    /// 否则后续用例触发 <c>Load</c>/<c>Reset</c>/<c>Apply</c> 时会回调进来，
    /// 而那里冒出的 LogError 会让不相干的用例失败（runner 对意外 Error 即判失败）。</item>
    /// </list>
    /// </remarks>
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

        /// <summary>
        /// 专供「重放取值抛异常」一例使用。<b>刻意与 <see cref="GameSettings"/> 分开</b>：
        /// 重放会遍历该类型下的全部句柄并逐个打日志，共用类型会让告警条数随其它用例增减而漂移。
        /// </summary>
        [Serializable]
        private sealed class NullableNestedSettings
        {
            public AudioSettings Audio = new();
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
        public void Ref_Subscribe_ImmediateCallbackThrows_SubscriptionIsReleased()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var invocations = 0;

            Action<float> handler = _ =>
            {
                invocations++;
                if (invocations == 1)
                    throw new InvalidOperationException("首次回调抛异常");
            };

            Assert.Throws<InvalidOperationException>(() => volume.Subscribe(handler),
                "立即回调抛异常应原样上抛");

            volume.Value = 0.5f;

            Assert.AreEqual(1, invocations,
                "回调抛异常后订阅必须已被清理;若泄漏,写值会再次触发该处理器");
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

        #region TryWriteValue

        [Test]
        public void Ref_TryWriteValue_AvailableType_WritesAndReturnsActual()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);

            Assert.IsTrue(volume.TryWriteValue(0.5f, out var actual), "类型可用时写入成功");
            Assert.AreEqual(0.5f, actual, 1e-5f, "回传写入后句柄解析到的实时值");
            Assert.AreEqual(0.5f, SettingsManager.Settings<GameSettings>().Audio.MasterVolume, 1e-5f,
                "写入要落回 POCO 字段");
        }

        [Test]
        public void Ref_TryWriteValue_UnregisteredType_ReturnsFalseWithoutThrowing()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            SettingsManager.Destroy(); // 句柄寿命长于管理器是既有设计

            bool ok = true;
            float actual = -1f;

            Assert.DoesNotThrow(() => ok = volume.TryWriteValue(0.25f, out actual),
                "设置类型不可用时不得抛异常——双向绑定会在 UI 事件回调里调用它，" +
                "异常会穿过 Slider.onValueChanged 抛进 UnityEvent");
            Assert.IsFalse(ok, "不可用时返回 false");
            Assert.AreEqual(0f, actual, 1e-5f, "不可用时 actual 为 default");
        }

        [Test]
        public void Ref_TryWriteValue_BeforeInitialize_ReturnsFalseWithoutThrowing()
        {
            SettingsManager.Destroy();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);

            bool ok = true;
            Assert.DoesNotThrow(() => ok = volume.TryWriteValue(0.25f, out _));
            Assert.IsFalse(ok, "尚未 Initialize 时同样按约定返回 false");
        }

        #endregion

        #region 实例替换后自动跟随与重放

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
        public void Ref_AfterReset_NotifiesSubscribersWithFactoryDefault()
        {
            var store = new MemoryStore
            {
                Data = new GameSettings { Audio = new AudioSettings { MasterVolume = 0.8f } },
                HasData = true,
            };
            SettingsManager.Initialize<GameSettings>(store,
                () => new GameSettings { Audio = new AudioSettings { MasterVolume = 0.3f } });
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var received = new List<float>();
            using var handle = volume.Subscribe(received.Add);
            received.Clear();

            SettingsManager.Reset<GameSettings>();

            CollectionAssert.AreEqual(new[] { 0.3f }, received,
                "玩家点「恢复默认」后绑定到句柄的 UI 必须跟着回到默认值");
        }

        [Test]
        public void Ref_AfterLoad_NotifiesSubscribers()
        {
            var store = Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var received = new List<float>();
            using var handle = volume.Subscribe(received.Add);
            received.Clear();

            store.Data = new GameSettings { Audio = new AudioSettings { MasterVolume = 0.8f } };
            store.HasData = true;
            SettingsManager.Load<GameSettings>();

            CollectionAssert.AreEqual(new[] { 0.8f }, received, "Load 换实例后订阅者收到新值");
        }

        [Test]
        public void Ref_AfterApply_NotifiesSubscribers()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var received = new List<float>();
            using var handle = volume.Subscribe(received.Add);
            received.Clear();

            SettingsManager.Apply(new GameSettings { Audio = new AudioSettings { MasterVolume = 0.6f } });

            CollectionAssert.AreEqual(new[] { 0.6f }, received, "Apply 换实例后订阅者收到新值");
        }

        [Test]
        public void Ref_Replay_ValueUnchanged_StillNotifies()
        {
            Init(); // 无持久化数据，Load 走 defaultFactory，值仍是 1f
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            var received = new List<float>();
            using var handle = volume.Subscribe(received.Add);
            received.Clear();

            SettingsManager.Load<GameSettings>();

            CollectionAssert.AreEqual(new[] { 1f }, received,
                "实例替换是无条件重放：数值未变也推一次。这是刻意的——可比较的只有「上一实例的值」，" +
                "而需要知道的是「订阅者上次收到什么」，二者在「直改 POCO 不通知」的缺口上分叉，" +
                "比较会漏掉陈旧的订阅者。勿改成去重");
        }

        [Test]
        public void Ref_Replay_PrecedesObserveNotification()
        {
            Init();
            var order = new List<string>();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);
            using var refHandle = volume.Subscribe(_ => order.Add("handle"));
            using var observeHandle = SettingsManager.Observe<GameSettings>(_ => order.Add("observe"));
            order.Clear();

            SettingsManager.Reset<GameSettings>();

            CollectionAssert.AreEqual(new[] { "handle", "observe" }, order,
                "字段句柄先收敛到新实例，再抛实例替换事件——上层消费者运行时字段视图已就位");
        }

        [Test]
        public void Ref_Replay_GetterThrows_DoesNotBreakApply()
        {
            SettingsManager.Initialize<NullableNestedSettings>(new MemoryStore(),
                () => new NullableNestedSettings());
            SettingsManager.Ref<NullableNestedSettings, float>(s => s.Audio.MasterVolume);

            // 中间段为 null：重放取值会抛。一个坏句柄不该让整次 Apply 失败
            LogAssert.Expect(LogType.Error, new Regex("字段句柄重放失败"));

            SettingsManager.Apply(new NullableNestedSettings { Audio = null });

            Assert.IsNull(SettingsManager.Settings<NullableNestedSettings>().Audio,
                "逐句柄隔离之后，实例替换照常完成");
        }

        [Test]
        public void Ref_SurvivesDestroyAndReinitialize()
        {
            Init();
            var volume = SettingsManager.Ref<GameSettings, float>(s => s.Audio.MasterVolume);

            SettingsManager.Destroy();
            SettingsManager.Initialize<GameSettings>(new MemoryStore(), () => new GameSettings());

            Assert.AreEqual(1f, volume.Value, 1e-5f, "句柄是常驻对象，不随管理器销毁而失效");

            var received = new List<float>();
            using var handle = volume.Subscribe(received.Add);
            received.Clear();

            SettingsManager.Reset<GameSettings>();

            CollectionAssert.AreEqual(new[] { 1f }, received,
                "重放注册表同样刻意不随 Destroy 清空：重建管理器后重放依然生效");
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
