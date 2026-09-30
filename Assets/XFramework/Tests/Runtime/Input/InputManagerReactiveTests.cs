using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XInput;
using XFramework.XInput.Messages;
using XFramework.XMessage;

namespace XFramework.XInput.Tests
{
    /// <summary>
    /// InputManager Observe* 系列响应式订阅测试。
    /// <para>用假 IInputProvider + 手动 <see cref="InputManager.Tick"/> 驱动帧脉冲,
    /// 锁定各 Observe 的触发、去重、首次必过与退订语义。</para>
    /// </summary>
    [TestFixture]
    public class InputManagerReactiveTests
    {
        #region Private

        /// <summary>可编程假 Provider:只实现 Observe* 测试用到的成员,其余抛 NotSupportedException。</summary>
        private sealed class FakeProvider : IInputProvider
        {
            public bool Pressed;
            public bool Released;
            public bool Held;
            public float Duration;
            public float FloatValue;
            public float FloatRawValue;
            public Vector2 Vector2Value;
            public Vector2 Vector2RawValue;

            /// <summary>是否被 Dispose 过(用于断言「参数校验失败不得先释放当前 provider」)。</summary>
            public bool Disposed;

            /// <summary>置真则 <see cref="Initialize"/> 抛异常(用于断言初始化失败不留中间态)。</summary>
            public bool ThrowOnInitialize;

            public void Initialize()
            {
                if (ThrowOnInitialize)
                    throw new InvalidOperationException("provider init failed");
            }

            /// <summary>置真则 <see cref="Tick"/> 抛异常(用于断言 provider 异常不会打死帧驱动)。</summary>
            public bool ThrowOnTick;

            public void Tick()
            {
                if (ThrowOnTick)
                    throw new InvalidOperationException("provider tick failed");
            }

            public bool HasAction(string action) => true;
            public bool WasPressedThisFrame(string action, uint playerId = 0) => Pressed;
            public bool WasReleasedThisFrame(string action, uint playerId = 0) => Released;
            public bool IsPressed(string action, uint playerId = 0) => Held;
            public float GetButtonPressDuration(string action, uint playerId = 0) => Duration;
            public float ReadFloat(string action, uint playerId = 0) => FloatValue;
            public float ReadFloatRaw(string action, uint playerId = 0) => FloatRawValue;
            public Vector2 ReadVector2(string action, uint playerId = 0) => Vector2Value;
            public Vector2 ReadVector2Raw(string action, uint playerId = 0) => Vector2RawValue;

            public GamepadType ActiveGamepadType => GamepadType.None;
            public InputDeviceType LastActiveDeviceType => InputDeviceType.None;
            public void Dispose() { Disposed = true; }

            public void SetVibration(uint playerId, float leftMotor, float rightMotor, float duration) => throw new NotSupportedException();
            public void StopVibration(uint playerId) => throw new NotSupportedException();
            public void StopAllVibration() => throw new NotSupportedException();
            public void SwitchActionMap(string mapName) => throw new NotSupportedException();
            public void EnableActionMap(string mapName) => throw new NotSupportedException();
            public void DisableActionMap(string mapName) => throw new NotSupportedException();
            public void DisableAllActionMaps() => throw new NotSupportedException();
            public string GetBindingDisplayString(string action, uint playerId = 0) => throw new NotSupportedException();
            public IReadOnlyList<InputBindingInfo> GetBindings(string action, uint playerId = 0) => throw new NotSupportedException();
            public string SaveBindingOverrides() => throw new NotSupportedException();
            public void LoadBindingOverrides(string data) => throw new NotSupportedException();
            public void ResetBindingOverrides(string action) => throw new NotSupportedException();
            public void ResetAllBindingOverrides() => throw new NotSupportedException();
            public IRebindingOperation StartRebinding(string action, string bindingId, uint playerId = 0) => throw new NotSupportedException();
        }

        /// <summary>
        /// 实现 <see cref="IDestroyCancellationToken"/> 的**普通 C# 对象**(非 MonoBehaviour),
        /// 用于验证「订阅随生命周期自动取消」对两类上下文都成立。
        /// <para>令牌在构造时取出缓存——<c>CancellationTokenSource.Token</c> 在源被 Dispose 之后不可读,
        /// 直接暴露 <c>_cts.Token</c> 会让自己销毁后反而读不出销毁令牌。</para>
        /// </summary>
        private sealed class FakeDestroyable : IDestroyCancellationToken
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private readonly CancellationToken _token;

            public FakeDestroyable() => _token = _cts.Token;

            public CancellationToken DestroyCancellationToken => _token;

            public void Destroy()
            {
                _cts.Cancel();
                _cts.Dispose();
            }
        }

        private static FakeProvider CreateProvider()
        {
            var provider = new FakeProvider();
            InputManager.SetProvider(provider);
            return provider;
        }

        #endregion

        #region 生命周期隔离

        [SetUp]
        public void SetUp() => InputManager.Destroy();

        [TearDown]
        public void TearDown() => InputManager.Destroy();

        #endregion

        #region ObservePressed / ObserveReleased

        [Test]
        public void ObservePressed_NoCallback_BeforeFirstTick()
        {
            var provider = CreateProvider();
            var calls = 0;
            var handle = InputManager.ObservePressed("Jump", () => calls++);

            // 帧脉冲未发布前(订阅后等下帧),不回调
            Assert.AreEqual(0, calls);
            handle.Dispose();
        }

        [Test]
        public void ObservePressed_TriggersOnce_PerPressedFrame()
        {
            var provider = CreateProvider();
            var calls = 0;
            var handle = InputManager.ObservePressed("Jump", () => calls++);

            provider.Pressed = true;
            InputManager.Tick();
            InputManager.Tick(); // 连续按下帧:每帧检测一次
            Assert.AreEqual(2, calls, "每帧检测一次,按下帧都回调");

            provider.Pressed = false;
            InputManager.Tick();
            Assert.AreEqual(2, calls, "未按下帧不回调");

            handle.Dispose();
        }

        [Test]
        public void ObservePressed_Dispose_StopsNotifications()
        {
            var provider = CreateProvider();
            var calls = 0;
            var handle = InputManager.ObservePressed("Jump", () => calls++);

            handle.Dispose();
            provider.Pressed = true;
            InputManager.Tick();

            Assert.AreEqual(0, calls, "退订后不再收到通知");
        }

        [Test]
        public void ObserveReleased_TriggersOnce_PerReleasedFrame()
        {
            var provider = CreateProvider();
            var calls = 0;
            var handle = InputManager.ObserveReleased("Jump", () => calls++);

            provider.Released = true;
            InputManager.Tick();
            Assert.AreEqual(1, calls);

            provider.Released = false;
            InputManager.Tick();
            Assert.AreEqual(1, calls, "未释放帧不回调");

            handle.Dispose();
        }

        #endregion

        #region ObserveHeld(去重)

        [Test]
        public void ObserveHeld_FirstTick_AlwaysCallback()
        {
            var provider = CreateProvider();
            var calls = new List<bool>();
            var handle = InputManager.ObserveHeld("Jump", calls.Add);

            // 首次必过:即使当前未按住(false)也回调一次
            provider.Held = false;
            InputManager.Tick();

            CollectionAssert.AreEqual(new[] { false }, calls, "首次必过");
            handle.Dispose();
        }

        [Test]
        public void ObserveHeld_ChangeOnly_Notified()
        {
            var provider = CreateProvider();
            var calls = new List<bool>();
            var handle = InputManager.ObserveHeld("Jump", calls.Add);

            provider.Held = false;
            InputManager.Tick();
            InputManager.Tick(); // 相同值去重
            provider.Held = true;
            InputManager.Tick();
            InputManager.Tick(); // 相同值去重
            provider.Held = false;
            InputManager.Tick();

            CollectionAssert.AreEqual(new[] { false, true, false }, calls, "仅状态变化时回调");
            handle.Dispose();
        }

        [Test]
        public void ObserveHeld_Dispose_StopsNotifications()
        {
            var provider = CreateProvider();
            var calls = new List<bool>();
            var handle = InputManager.ObserveHeld("Jump", calls.Add);

            InputManager.Tick();
            handle.Dispose();
            provider.Held = true;
            InputManager.Tick();

            CollectionAssert.AreEqual(new[] { false }, calls, "退订后不再收到通知");
        }

        #endregion

        #region ObservePressDuration / 轴输入(去重)

        [Test]
        public void ObservePressDuration_ChangeOnly_Notified()
        {
            var provider = CreateProvider();
            var calls = new List<float>();
            var handle = InputManager.ObservePressDuration("Jump", calls.Add);

            provider.Duration = 0.5f;
            InputManager.Tick();
            InputManager.Tick(); // 相同值去重
            provider.Duration = 1.2f;
            InputManager.Tick();

            CollectionAssert.AreEqual(new[] { 0.5f, 1.2f }, calls, "仅时长变化时回调");
            handle.Dispose();
        }

        [Test]
        public void ObserveVector2_ChangeOnly_Notified()
        {
            var provider = CreateProvider();
            var calls = new List<Vector2>();
            var handle = InputManager.ObserveVector2("Move", calls.Add);

            provider.Vector2Value = new Vector2(1f, 0f);
            InputManager.Tick();
            InputManager.Tick(); // 相同值去重
            provider.Vector2Value = new Vector2(0f, 1f);
            InputManager.Tick();

            CollectionAssert.AreEqual(new[] { new Vector2(1f, 0f), new Vector2(0f, 1f) }, calls, "仅值变化时回调");
            handle.Dispose();
        }

        [Test]
        public void ObserveVector2Raw_ChangeOnly_Notified()
        {
            var provider = CreateProvider();
            var calls = new List<Vector2>();
            var handle = InputManager.ObserveVector2Raw("Move", calls.Add);

            provider.Vector2RawValue = new Vector2(2f, 0f);
            InputManager.Tick();
            InputManager.Tick(); // 相同值去重

            CollectionAssert.AreEqual(new[] { new Vector2(2f, 0f) }, calls, "原始值变化时回调,相同值去重");
            handle.Dispose();
        }

        [Test]
        public void ObserveFloat_ChangeOnly_Notified()
        {
            var provider = CreateProvider();
            var calls = new List<float>();
            var handle = InputManager.ObserveFloat("Throttle", calls.Add);

            provider.FloatValue = 0.3f;
            InputManager.Tick();
            InputManager.Tick(); // 相同值去重
            provider.FloatValue = 0.8f;
            InputManager.Tick();

            CollectionAssert.AreEqual(new[] { 0.3f, 0.8f }, calls, "仅值变化时回调");
            handle.Dispose();
        }

        [Test]
        public void ObserveFloatRaw_ChangeOnly_Notified()
        {
            var provider = CreateProvider();
            var calls = new List<float>();
            var handle = InputManager.ObserveFloatRaw("Throttle", calls.Add);

            provider.FloatRawValue = 0.6f;
            InputManager.Tick();
            InputManager.Tick(); // 相同值去重

            CollectionAssert.AreEqual(new[] { 0.6f }, calls, "原始值变化时回调,相同值去重");
            handle.Dispose();
        }

        #endregion

        #region 参数与空 Provider 防御

        [Test]
        public void Observe_NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => InputManager.ObservePressed("Jump", null));
            Assert.Throws<ArgumentNullException>(() => InputManager.ObserveReleased("Jump", null));
            Assert.Throws<ArgumentNullException>(() => InputManager.ObserveHeld("Jump", null));
            Assert.Throws<ArgumentNullException>(() => InputManager.ObserveVector2("Move", null));
            Assert.Throws<ArgumentNullException>(() => InputManager.ObserveFloatRaw("Throttle", null));
            // 补齐另外三个:此前 8 个 Observe* 只断言了 5 个的 null 守卫
            Assert.Throws<ArgumentNullException>(() => InputManager.ObservePressDuration("Jump", null));
            Assert.Throws<ArgumentNullException>(() => InputManager.ObserveFloat("Throttle", null));
            Assert.Throws<ArgumentNullException>(() => InputManager.ObserveVector2Raw("Look", null));
        }

        [Test]
        public void Observe_WithoutProvider_NoCallbackNoThrow()
        {
            // 未 SetProvider:provider 为 null,Observe 仍可订阅,Tick 不崩且读默认值(首次必过)
            var calls = new List<bool>();
            var handle = InputManager.ObserveHeld("Jump", calls.Add);

            InputManager.Tick();
            InputManager.Tick();

            CollectionAssert.AreEqual(new[] { false }, calls, "无 Provider 时首次回调 false,不抛异常");
            handle.Dispose();
        }

        #endregion

        #region 门面状态机与空值语义

        [Test]
        public void GetBindings_NotInitialized_ReturnsEmptyList()
        {
            // 未 SetProvider:provider 为 null。查询类 API 一律给空引用安全的默认值,列表类给空列表
            // (CLAUDE.md 的「宽容语义豁免」把这条记为有意设计,此处把它锁住)
            var bindings = InputManager.GetBindings("Jump");

            Assert.IsNotNull(bindings, "未初始化时不得返回 null:查询类 API 的约定是空引用安全");
            Assert.AreEqual(0, bindings.Count, "且应为空列表,调用方可直接 foreach");
        }

        [Test]
        public void SetProvider_Null_Throws_AndKeepsCurrentProvider()
        {
            var provider = CreateProvider();

            Assert.Throws<ArgumentNullException>(() => InputManager.SetProvider(null));

            Assert.IsFalse(provider.Disposed, "参数校验失败不得先把当前 provider 释放掉");
            Assert.IsTrue(InputManager.IsInitialized, "管理器应仍处于可用态");
            provider.Pressed = true;
            Assert.IsTrue(InputManager.WasPressedThisFrame("Jump"), "原 provider 应仍在位,查询照常转发");
        }

        [Test]
        public void Initialize_CustomProviderThrows_LeavesUninitialized_AndDoesNotRoute()
        {
            var bad = new FakeProvider { ThrowOnInitialize = true, Pressed = true };

            Assert.Throws<InvalidOperationException>(() => InputManager.Initialize(bad));

            Assert.IsFalse(InputManager.IsInitialized, "provider 初始化失败后管理器应仍是未初始化态");
            Assert.IsFalse(InputManager.WasPressedThisFrame("Jump"),
                "未初始化态下查询必须走默认值,不得转发到半初始化的 provider(它的 Pressed 是 true)");
        }

        #endregion

        #region 订阅生命周期绑定

        [Test]
        public void ObservePressed_IDestroyCancellationTokenContext_AutoUnsubscribes()
        {
            // 改前必红是**编译级**的:旧的 context 形参是 MonoBehaviour,传下面这个普通对象直接 CS1503
            var provider = CreateProvider();
            var context = new FakeDestroyable();
            var calls = 0;
            InputManager.ObservePressed("Jump", () => calls++, context);

            provider.Pressed = true;
            InputManager.Tick();
            Assert.AreEqual(1, calls, "订阅后按下应回调");

            context.Destroy();
            InputManager.Tick();
            Assert.AreEqual(1, calls, "销毁令牌取消后应已自动退订,不再回调");
        }

        [Test]
        public void Subscribe_IDestroyCancellationTokenContext_AutoUnsubscribes()
        {
            var context = new FakeDestroyable();
            var calls = 0;
            InputManager.Subscribe((DeviceConnectedMessage _) => calls++, context);

            MessageManager.Publish(new DeviceConnectedMessage("pad", 1, true));
            Assert.AreEqual(1, calls, "订阅后应收到消息");

            context.Destroy();
            MessageManager.Publish(new DeviceConnectedMessage("pad", 1, true));
            Assert.AreEqual(1, calls, "销毁令牌取消后应已自动退订,不再回调");
        }

        [Test]
        public void Observe_NonBindableContext_WarnsInsteadOfSilentlyNotBinding()
        {
            CreateProvider();

            // 这是**新增行为**,不是回归——改前传非 MonoBehaviour 编译都过不了,谈不上有告警。
            // 锁住它是为了让「订上了但没人会在销毁时退订」这种情形永远留痕,而不是静默。
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Input\] context of type 'Object' is neither"));
            var handle = InputManager.ObservePressed("Jump", () => { }, new object());

            handle.Dispose();
        }

        #endregion

        #region 帧驱动的异常隔离

        [Test]
        public void Tick_ProviderThrows_StillPublishesFramePulse()
        {
            var provider = CreateProvider();
            var calls = 0;
            var handle = InputManager.ObservePressed("Jump", () => calls++);
            provider.Pressed = true;
            provider.ThrowOnTick = true;

            LogAssert.Expect(LogType.Error, new Regex(@"\[Input\] Provider\.Tick threw exception"));

            Assert.DoesNotThrow(() => InputManager.Tick(), "provider 的异常不得从 Tick 逃逸");
            Assert.AreEqual(1, calls, "帧脉冲必须照常发布——否则全部 Observe* 会静默哑掉");

            handle.Dispose();
        }

        #endregion
    }
}
