using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// 会话级复位的回归用例（**改前必红**）：关闭域重载时，上一播放会话的静态字段存活到第二会话。
    /// <para><b>它锁的缺陷</b>：<see cref="SettingRefRegistry{T}"/> 的重放表只增不减、且刻意不随
    /// <see cref="SettingsManager.Destroy"/> 清空（那是既有设计），于是第二会话里「同一字段再建一次句柄」
    /// 会让表里出现两个句柄——重放两次、同一条错误日志写两遍；而 <see cref="SettingsManager.Initialize{T}(ISettingsStore, Func{T}, SettingsOptions)"/>
    /// 在第二会话只会打一条 Warning 就返回上一会话的旧实例，使用方无法自救。</para>
    /// <para><b>边界语义（本文件钉住的契约）</b>：会话边界清空重放表；<b>被使用过</b>（读 / 写 /
    /// <c>TryWriteValue</c> / <c>Subscribe</c>）的句柄自动重新入表并照常重放，没人再用的句柄自然出表。
    /// 这四条「用即重登记」用例同时是那条维护不变量的守卫——将来给 <c>SettingRef</c> 加新成员却忘了调
    /// <c>EnsureRegistered()</c>，对应的变体会红。</para>
    /// <para><b>为什么另开 fixture</b>：<c>SettingRefTests</c> 的纪律是「重放表进程级共享、日志条数敏感」，
    /// 而本文件的用例会主动清表，混在一起会互相干扰。各用例专用的设置类型保持私有嵌套。</para>
    /// </summary>
    [TestFixture]
    public class SettingsSessionResetTests
    {
        #region Test Doubles

        [Serializable]
        private sealed class AudioSettings
        {
            public float MasterVolume = 1f;
        }

        [Serializable]
        private sealed class SessionSettings
        {
            public AudioSettings Audio = new();
        }

        /// <summary>
        /// 专供「重放取值抛异常」一例：中间段可被置 <c>null</c>。
        /// <b>刻意与 <see cref="SessionSettings"/> 分开</b>——重放会遍历该类型下全部句柄并逐个打日志，
        /// 共用类型会让日志条数随其它用例增减而漂移。
        /// </summary>
        [Serializable]
        private sealed class NullableSessionSettings
        {
            public AudioSettings Audio = new();
        }

        /// <summary>专供「切后台宿主引用计数」一例。</summary>
        [Serializable]
        private sealed class PauseSessionSettings
        {
            public float Volume = 1f;
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

        /// <summary>
        /// 模拟「退出播放 → 第二会话的 <c>SubsystemRegistration</c>」对 Settings 做的事。
        /// <para><b>为什么用反射：</b>改前门面没有这个钩子——直接调用会让整个文件编译不过，
        /// 就拿不到「改前必红」的证据。钩子缺失时返回 <c>false</c>，由用例显式断言（而不是靠编译错误）。</para>
        /// </summary>
        private static bool SimulateSessionBoundary()
        {
            var hook = typeof(SettingsManager).GetMethod(
                "ResetForNewSession", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (hook == null)
            {
                return false;
            }

            hook.Invoke(null, null);
            return true;
        }

        #endregion

        #region 会话边界

        [Test]
        public void SessionBoundary_StaleHandle_DoesNotReplayTwice()
        {
            // 清场：把上一个用例留在表里的句柄清出去（改前是空操作，故每个用例自证前置）
            SimulateSessionBoundary();

            // 会话 1：建一个句柄。它就是「上一会话遗留、第二会话无人再用」的那一类
            SettingsManager.Initialize<NullableSessionSettings>(new MemoryStore(), () => new NullableSessionSettings());
            SettingsManager.Ref<NullableSessionSettings, float>(s => s.Audio.MasterVolume);

            Assert.IsTrue(SimulateSessionBoundary(),
                "SettingsManager 必须在会话边界复位（ResetForNewSession + RuntimeInitializeOnLoadMethod(SubsystemRegistration)）");

            // 会话 2：同一类型再建一次句柄——真实项目里新对象 / 新 UI 重建时必然发生
            SettingsManager.Initialize<NullableSessionSettings>(new MemoryStore(), () => new NullableSessionSettings());
            SettingsManager.Ref<NullableSessionSettings, float>(s => s.Audio.MasterVolume);

            // 恰好一条：重放表若没在会话边界清空，上一会话那个坏句柄会跟着重放 → 两条
            // → 多出的那条判「Unhandled log message」，正是手工验收里第二遍红的那个形状
            LogAssert.Expect(LogType.Error, new Regex("字段句柄重放失败"));
            SettingsManager.Apply(new NullableSessionSettings { Audio = null });
        }

        /// <summary>
        /// 钉住现状（SET-004）：边界只清句柄登记表，**不清句柄自身的订阅流**——会话 1 的订阅者仍在流上，
        /// 句柄在会话 2 被读即重新入表并照常重放给旧订阅者。README/CHANGELOG 已按此订正
        /// （「旧订阅者不会复活」是错的）；彻底清流属破坏性设计，仍待裁。
        /// </summary>
        [Test]
        public void SessionBoundary_HandleReadInNewSession_StillReplays()
        {
            SimulateSessionBoundary();

            SettingsManager.Initialize<SessionSettings>(new MemoryStore(), () => new SessionSettings());
            var volume = SettingsManager.Ref<SessionSettings, float>(s => s.Audio.MasterVolume);

            var received = new List<float>();
            using var subscription = volume.Subscribe(received.Add);
            received.Clear();

            SimulateSessionBoundary();

            SettingsManager.Initialize<SessionSettings>(new MemoryStore(), () => new SessionSettings());
            _ = volume.Value; // 会话 2 的「使用」＝读

            SettingsManager.Reset<SessionSettings>();

            CollectionAssert.AreEqual(new[] { 1f }, received,
                "会话 2 里被读过的句柄必须『用即重登记』并照常重放；收到空集说明退化成了「会话边界后永不重放」的静默降级");
        }

        [Test]
        public void SessionBoundary_HandleWrittenInNewSession_StillReplays()
        {
            SimulateSessionBoundary();

            SettingsManager.Initialize<SessionSettings>(new MemoryStore(), () => new SessionSettings());
            var volume = SettingsManager.Ref<SessionSettings, float>(s => s.Audio.MasterVolume);

            var received = new List<float>();
            using var subscription = volume.Subscribe(received.Add);
            received.Clear();

            SimulateSessionBoundary();

            SettingsManager.Initialize<SessionSettings>(new MemoryStore(), () => new SessionSettings());
            volume.Value = 0.5f; // 会话 2 的「使用」＝写
            received.Clear();

            SettingsManager.Reset<SessionSettings>();

            CollectionAssert.AreEqual(new[] { 1f }, received,
                "会话 2 里被写过的句柄必须『用即重登记』并照常重放");
        }

        [Test]
        public void SessionBoundary_HandleTryWrittenInNewSession_StillReplays()
        {
            SimulateSessionBoundary();

            SettingsManager.Initialize<SessionSettings>(new MemoryStore(), () => new SessionSettings());
            var volume = SettingsManager.Ref<SessionSettings, float>(s => s.Audio.MasterVolume);

            var received = new List<float>();
            using var subscription = volume.Subscribe(received.Add);
            received.Clear();

            SimulateSessionBoundary();

            SettingsManager.Initialize<SessionSettings>(new MemoryStore(), () => new SessionSettings());
            Assert.IsTrue(volume.TryWriteValue(0.5f, out _), "前提校验：类型可用时 TryWriteValue 应当写入成功");
            received.Clear();

            SettingsManager.Reset<SessionSettings>();

            CollectionAssert.AreEqual(new[] { 1f }, received,
                "会话 2 里经 TryWriteValue 用过的句柄必须『用即重登记』并照常重放");
        }

        [Test]
        public void SessionBoundary_HandleSubscribedInNewSession_StillReplays()
        {
            SimulateSessionBoundary();

            SettingsManager.Initialize<SessionSettings>(new MemoryStore(), () => new SessionSettings());
            var volume = SettingsManager.Ref<SessionSettings, float>(s => s.Audio.MasterVolume);

            SimulateSessionBoundary();

            SettingsManager.Initialize<SessionSettings>(new MemoryStore(), () => new SessionSettings());

            // 会话 2 的「使用」＝订阅：现实中就是新 UI 重建时重新绑定
            var received = new List<float>();
            using var subscription = volume.Subscribe(received.Add);
            received.Clear();

            SettingsManager.Reset<SessionSettings>();

            CollectionAssert.AreEqual(new[] { 1f }, received,
                "会话 2 里被订阅过的句柄必须『用即重登记』并照常重放");
        }

        [Test]
        public void SessionBoundary_PauseNotifierRefCount_Resets()
        {
            SimulateSessionBoundary();

            // 会话 1：开了 SaveOnPause 的类型申请了宿主。它的管理器**不会被 Dispose**——
            // 会话边界只丢引用（与七门面同形），那份计数若不清就是永久泄漏
            SettingsManager.Initialize<PauseSessionSettings>(new MemoryStore(), () => new PauseSessionSettings(),
                new SettingsOptions { SaveOnPause = true });
            Assert.IsNotNull(SettingsPauseNotifier.Instance, "前提校验：开启 SaveOnPause 应当申请到宿主");

            SimulateSessionBoundary();

            // 会话 2：同类型重新初始化（照常申请宿主），随后按正常流程 Dispose
            var manager = SettingsManager.Initialize<PauseSessionSettings>(new MemoryStore(), () => new PauseSessionSettings(),
                new SettingsOptions { SaveOnPause = true });
            manager.Dispose();

            Assert.IsTrue(SettingsPauseNotifier.Instance == null,
                "会话 1 的计数若不随会话边界复位，会话 2 唯一一次 Release 也归不了零——DontDestroyOnLoad 宿主将永不销毁、计数逐会话累积");
        }

        [Test]
        public void SessionBoundary_DestroyStillKeepsRegistry()
        {
            // 反向锁死既有设计：Destroy 不是会话边界，重放表不随它清空
            // （与 SettingRefTests.Ref_SurvivesDestroyAndReinitialize 同一契约，这里从「边界清、Destroy 不清」的对照面再钉一次）
            SimulateSessionBoundary();

            SettingsManager.Initialize<SessionSettings>(new MemoryStore(), () => new SessionSettings());
            var volume = SettingsManager.Ref<SessionSettings, float>(s => s.Audio.MasterVolume);

            SettingsManager.Destroy();
            SettingsManager.Initialize<SessionSettings>(new MemoryStore(), () => new SessionSettings());

            var received = new List<float>();
            using var subscription = volume.Subscribe(received.Add);
            received.Clear();

            SettingsManager.Reset<SessionSettings>();

            CollectionAssert.AreEqual(new[] { 1f }, received,
                "Destroy 不构成会话边界：句柄不随管理器销毁而失效，重建后重放依然生效");
        }

        #endregion
    }
}
