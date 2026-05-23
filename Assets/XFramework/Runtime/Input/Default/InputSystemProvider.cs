using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace XFramework.XInput.Default
{
    /// <summary>
    /// 基于 Unity Input System 的 <see cref="IInputProvider"/> 实现。
    /// <para>自动加载 <c>InputSystem_Actions.inputactions</c> 并提供纯字符串驱动的输入访问。</para>
    /// <para>支持手柄类型自动检测（Xbox / PS4 / PS5 / Switch Pro）。</para>
    /// <para>不包含任何游戏专属动作定义（如 Jump、Attack），第三方游戏需自行定义输入封装。</para>
    /// </summary>
    public sealed class InputSystemProvider : IInputProvider
    {
        #region Private Fields

        private InputActionAsset _actionAsset;
        private string _currentMapName;
        private GamepadType _activeGamepadType;
        private InputDeviceType _lastActiveDeviceType;

        // 通用动作字典缓存：按需懒加载 Action 引用，避免每帧全量字符串查找
        private readonly Dictionary<string, InputAction> _actionCache = new Dictionary<string, InputAction>(32);

        // 振动相关
        private Coroutine _vibrationCoroutine;
        private MonoBehaviour _coroutineRunner;
        private float _vibrationLeftMotor;
        private float _vibrationRightMotor;

        // 长按计时：记录每个动作首次按下时间（按需增长，支持任意动作名）
        private readonly Dictionary<string, float> _buttonPressStartTimes = new Dictionary<string, float>(32);

        #endregion

        #region Properties

        public GamepadType ActiveGamepadType => _activeGamepadType;
        public InputDeviceType LastActiveDeviceType => _lastActiveDeviceType;

        #endregion

        #region Events

        public event Action<GamepadType> OnGamepadTypeChanged;
        public event Action OnDeviceConnected;
        public event Action OnDeviceDisconnected;

        #endregion

        #region Initialize

        public void Initialize()
        {
            // 加载 Input Action Asset
            _actionAsset = Resources.Load<InputActionAsset>("InputSystem_Actions");

            if (_actionAsset == null)
            {
                Debug.LogError("[InputSystemProvider] Failed to load InputSystem_Actions.inputactions. " +
                               "Make sure it exists under Assets/Resources/");
                return;
            }

            _actionAsset.Enable();

            // 监听设备变更
            InputSystem.onDeviceChange += OnInputDeviceChange;

            // 默认启用 Player map
            SwitchActionMap("Player");

            Debug.Log("[InputSystemProvider] Initialized successfully.");
        }

        /// <summary>
        /// 设置协程宿主，用于振动持续时间的协程调度。
        /// </summary>
        public void SetCoroutineRunner(MonoBehaviour runner)
        {
            _coroutineRunner = runner;
        }

        #endregion

        #region Tick

        public void Tick()
        {
            if (_actionAsset == null) return;

            // 检测手柄类型变化
            DetectGamepadType();

            // 检测最近活跃设备类型
            DetectActiveDeviceType();

            // 刷新所有已访问 Action 的长按计时
            UpdateButtonPressDurations();
        }

        #endregion

        #region Button Events

        public bool WasPressedThisFrame(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            return inputAction?.WasPressedThisFrame() ?? false;
        }

        public bool WasReleasedThisFrame(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            return inputAction?.WasReleasedThisFrame() ?? false;
        }

        public bool IsPressed(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            return inputAction?.IsPressed() ?? false;
        }

        #endregion

        #region Value Input

        public Vector2 ReadVector2(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            return inputAction?.ReadValue<Vector2>() ?? Vector2.zero;
        }

        public float ReadFloat(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            return inputAction?.ReadValue<float>() ?? 0f;
        }

        public float ReadFloatRaw(string action, uint playerId = 0)
        {
            // InputAction.ReadValue<float>() 在 Unity Input System 中已返回 raw value
            var inputAction = GetAction(action);
            return inputAction?.ReadValue<float>() ?? 0f;
        }

        public Vector2 ReadVector2Raw(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            return inputAction?.ReadValue<Vector2>() ?? Vector2.zero;
        }

        #endregion

        #region Press Duration

        public float GetButtonPressDuration(string action, uint playerId = 0)
        {
            if (_buttonPressStartTimes.TryGetValue(action, out var startTime) && startTime > 0f)
            {
                return Time.unscaledTime - startTime;
            }
            return 0f;
        }

        /// <summary>
        /// 每帧刷新所有已访问 Action 的长按计时。
        /// </summary>
        private void UpdateButtonPressDurations()
        {
            foreach (var kvp in _actionCache)
            {
                var actionName = kvp.Key;
                var inputAction = kvp.Value;
                if (inputAction == null) continue;

                if (inputAction.WasPressedThisFrame())
                {
                    _buttonPressStartTimes[actionName] = Time.unscaledTime;
                }
                else if (inputAction.WasReleasedThisFrame())
                {
                    _buttonPressStartTimes[actionName] = 0f;
                }
            }
        }

        #endregion

        #region ActionMap Switch

        public void SwitchActionMap(string mapName)
        {
            DisableAllActionMaps();
            EnableActionMap(mapName);
        }

        public void EnableActionMap(string mapName)
        {
            if (_actionAsset == null) return;

            var targetMap = _actionAsset.FindActionMap(mapName);
            if (targetMap != null)
            {
                targetMap.Enable();
                _currentMapName = mapName;

                Debug.Log($"[InputSystemProvider] Enabled action map: {mapName}");
            }
            else
            {
                Debug.LogWarning($"[InputSystemProvider] ActionMap '{mapName}' not found.");
            }
        }

        public void DisableActionMap(string mapName)
        {
            if (_actionAsset == null) return;

            var targetMap = _actionAsset.FindActionMap(mapName);
            if (targetMap != null)
            {
                targetMap.Disable();
                Debug.Log($"[InputSystemProvider] Disabled action map: {mapName}");
            }
        }

        public void DisableAllActionMaps()
        {
            if (_actionAsset == null) return;

            foreach (var map in _actionAsset.actionMaps)
            {
                map.Disable();
            }

            Debug.Log("[InputSystemProvider] All action maps disabled.");
        }

        #endregion

        #region Vibration

        public void SetVibration(uint playerId, float leftMotor, float rightMotor, float duration)
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return;

            // 限制范围 [0, 1]
            leftMotor = Mathf.Clamp01(leftMotor);
            rightMotor = Mathf.Clamp01(rightMotor);

            _vibrationLeftMotor = leftMotor;
            _vibrationRightMotor = rightMotor;

            gamepad.SetMotorSpeeds(leftMotor, rightMotor);

            // 停止之前的振动协程
            if (_vibrationCoroutine != null && _coroutineRunner != null)
            {
                _coroutineRunner.StopCoroutine(_vibrationCoroutine);
                _vibrationCoroutine = null;
            }

            // 如果指定了持续时间，启动协程自动停止
            if (duration > 0f && _coroutineRunner != null)
            {
                _vibrationCoroutine = _coroutineRunner.StartCoroutine(VibrationTimer(duration));
            }
        }

        public void StopVibration(uint playerId)
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return;

            gamepad.SetMotorSpeeds(0f, 0f);
            _vibrationLeftMotor = 0f;
            _vibrationRightMotor = 0f;

            if (_vibrationCoroutine != null && _coroutineRunner != null)
            {
                _coroutineRunner.StopCoroutine(_vibrationCoroutine);
                _vibrationCoroutine = null;
            }
        }

        public void StopAllVibration()
        {
            StopVibration(0);
        }

        private IEnumerator VibrationTimer(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                gamepad.SetMotorSpeeds(0f, 0f);
            }

            _vibrationLeftMotor = 0f;
            _vibrationRightMotor = 0f;
            _vibrationCoroutine = null;
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            InputSystem.onDeviceChange -= OnInputDeviceChange;

            StopAllVibration();

            if (_actionAsset != null)
            {
                _actionAsset.Disable();
                _actionAsset = null;
            }

            _actionCache.Clear();
            _buttonPressStartTimes.Clear();

            Debug.Log("[InputSystemProvider] Disposed.");
        }

        #endregion

        #region Private — Device Management

        private void OnInputDeviceChange(InputDevice device, InputDeviceChange change)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                    OnDeviceConnected?.Invoke();
                    break;

                case InputDeviceChange.Removed:
                    OnDeviceDisconnected?.Invoke();
                    break;

                case InputDeviceChange.Reconnected:
                    OnDeviceConnected?.Invoke();
                    break;

                case InputDeviceChange.Disconnected:
                    OnDeviceDisconnected?.Invoke();
                    break;
            }
        }

        private void DetectGamepadType()
        {
            var currentType = _activeGamepadType;

            var gamepad = Gamepad.current;
            if (gamepad == null)
            {
                currentType = GamepadType.None;
            }
            else
            {
                currentType = gamepad.displayName switch
                {
                    string name when name.Contains("DualShock 4") || (name.Contains("Wireless Controller") && name.Length < 20) => GamepadType.PlayStation4,
                    string name when name.Contains("DualSense") => GamepadType.PlayStation5,
                    string name when name.Contains("Switch") || name.Contains("Nintendo") => GamepadType.SwitchPro,
                    string name when name.Contains("Xbox") || name.Contains("XInput") => GamepadType.Xbox,
                    _ => GamepadType.Generic
                };
            }

            if (currentType != _activeGamepadType)
            {
                _activeGamepadType = currentType;
                OnGamepadTypeChanged?.Invoke(currentType);
            }
        }

        private void DetectActiveDeviceType()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var touchscreen = Touchscreen.current;
            var gamepad = Gamepad.current;

            // 检查最近是否有键盘/鼠标输入
            if (keyboard != null && keyboard.wasUpdatedThisFrame)
                _lastActiveDeviceType = InputDeviceType.KeyboardMouse;
            else if (mouse != null && mouse.wasUpdatedThisFrame)
                _lastActiveDeviceType = InputDeviceType.KeyboardMouse;
            else if (gamepad != null && gamepad.wasUpdatedThisFrame)
                _lastActiveDeviceType = InputDeviceType.Gamepad;
            else if (touchscreen != null && touchscreen.wasUpdatedThisFrame)
                _lastActiveDeviceType = InputDeviceType.Touch;
        }

        #endregion

        #region Private — Action Cache

        /// <summary>
        /// 从当前启用的 ActionMap 中查找指定名称的 Action。
        /// 先查缓存，再遍历所有已启用的 ActionMap 并缓存结果。
        /// </summary>
        private InputAction GetAction(string actionName)
        {
            if (_actionAsset == null) return null;

            // 从缓存中查找
            if (_actionCache.TryGetValue(actionName, out var cachedAction))
            {
                if (cachedAction != null) return cachedAction;
                _actionCache.Remove(actionName);
            }

            // 在所有已启用的 ActionMap 中查找
            foreach (var map in _actionAsset.actionMaps)
            {
                if (map.enabled)
                {
                    var action = map.FindAction(actionName);
                    if (action != null)
                    {
                        _actionCache[actionName] = action;
                        return action;
                    }
                }
            }

            // 如果没有找到，尝试在所有 ActionMap 中查找（作为后备）
            var fallback = _actionAsset.FindAction(actionName);
            if (fallback != null)
            {
                _actionCache[actionName] = fallback;
            }
            return fallback;
        }

        #endregion
    }
}