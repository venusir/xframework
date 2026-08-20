using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using XFramework.XInput;

namespace XFramework.XInput.Tests
{
    /// <summary>
    /// InputManager Observe* 系列响应式订阅测试(移除 R3 依赖计划 Phase 5)。
    /// <para>用假 IInputProvider + 手动 <see cref="InputManager.Tick"/> 驱动帧脉冲,
    /// 锁定各 Observe 的触发、去重、首次必过与退订语义(与原 R3 EveryUpdate 链一致)。</para>
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

            public void Initialize() { }
            public void Tick() { }

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
            public void Dispose() { }

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

            // 帧脉冲未发布前(等价于 R3 EveryUpdate 热流:订阅后等下帧),不回调
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

            // 首次必过:即使当前未按住(false)也回调一次(与 R3 DistinctUntilChanged 语义一致)
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
    }
}
