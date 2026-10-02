using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XInput.Tests
{
    /// <summary>
    /// 可编程的假 <see cref="IInputProvider"/>：实现全部成员，只让测试真正用到的那些可编程，
    /// 其余抛 <see cref="NotSupportedException"/>。
    /// <para><b>为什么是独立文件</b>：它被多个夹具共用（输入门面的响应式测试、引导阶段测试……），
    /// 嵌在某个夹具里就只能被那一个用——照 <c>FakeAudioClipLoader</c> / <c>FakeAssetManager</c> 的体例。</para>
    /// </summary>
    internal sealed class FakeInputProvider : IInputProvider
    {
        public bool Pressed;
        public bool Released;
        public bool Held;
        public float Duration;
        public float FloatValue;
        public float FloatRawValue;
        public Vector2 Vector2Value;
        public Vector2 Vector2RawValue;

        /// <summary>是否被 Dispose 过（用于断言「参数校验失败不得先释放当前 provider」）。</summary>
        public bool Disposed;

        /// <summary>置真则 <see cref="Initialize"/> 抛异常（用于断言初始化失败不留中间态）。</summary>
        public bool ThrowOnInitialize;

        public void Initialize()
        {
            if (ThrowOnInitialize)
                throw new InvalidOperationException("provider init failed");
        }

        /// <summary>置真则 <see cref="Tick"/> 抛异常（用于断言 provider 异常不会打死帧驱动）。</summary>
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
}
