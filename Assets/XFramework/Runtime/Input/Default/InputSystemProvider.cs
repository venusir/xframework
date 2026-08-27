using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using XFramework.XReactive;

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

        #region Initialize

        public void Initialize()
        {
            // 加载 Input Action Asset(资源须位于 Assets/Resources/)
            _actionAsset = Resources.Load<InputActionAsset>("InputSystem_Actions");

            if (_actionAsset == null)
            {
                // 显式抛异常而非静默返回:加载失败后所有读取会静默返回默认值,掩盖配置错误
                throw new InvalidOperationException(
                    "[Input] 加载 InputSystem_Actions.inputactions 失败:请将 InputSystem_Actions.inputactions 放入 Assets/Resources/ 目录后重试");
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

        #region 绑定查询

        public string GetBindingDisplayString(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            if (inputAction == null) return string.Empty;

            // 根据当前活跃设备类型选择合适的 Control Scheme
            var bindingIndex = GetEffectiveBindingIndex(inputAction);
            if (bindingIndex < 0) return string.Empty;

            return inputAction.GetBindingDisplayString(bindingIndex) ?? string.Empty;
        }

        public IReadOnlyList<InputBindingInfo> GetBindings(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            if (inputAction == null) return Array.Empty<InputBindingInfo>();

            var bindings = inputAction.bindings;
            // 先收集符合条件的绑定索引（排除复合结构本身及其子项）
            var validIndices = new List<int>(bindings.Count);
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                validIndices.Add(i);
            }

            var result = new InputBindingInfo[validIndices.Count];
            for (int i = 0; i < validIndices.Count; i++)
            {
                var idx = validIndices[i];
                var b = bindings[idx];
                result[i] = new InputBindingInfo
                {
                    Id = b.id.ToString(),
                    DisplayName = inputAction.GetBindingDisplayString(idx) ?? string.Empty,
                    Group = b.groups,
                    IsComposite = false,
                    IsPartOfComposite = false,
                    IsOverridden = !string.IsNullOrEmpty(b.overridePath)
                };
            }

            return result;
        }

        /// <summary>
        /// 根据当前活跃设备类型，选择一个有效的 binding index。
        /// <para>优先选择当前设备 Control Scheme 下的第一个绑定，否则回退到第一个非 composite 绑定。</para>
        /// </summary>
        private int GetEffectiveBindingIndex(InputAction inputAction)
        {
            var bindings = inputAction.bindings;
            if (bindings.Count == 0) return -1;

            // 尝试按当前活跃设备类型匹配 Control Scheme
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;

                if (MatchesActiveDevice(b.groups))
                    return i;
            }

            // 回退：返回第一个非 composite 且非 part 的绑定
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                return i;
            }

            return -1;
        }

        private bool MatchesActiveDevice(string group)
        {
            return _lastActiveDeviceType switch
            {
                InputDeviceType.KeyboardMouse => group.Contains("Keyboard&Mouse", StringComparison.OrdinalIgnoreCase),
                InputDeviceType.Gamepad => group.Contains("Gamepad", StringComparison.OrdinalIgnoreCase),
                InputDeviceType.Touch => group.Contains("Touch", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        #endregion

        #region 绑定持久化

        public string SaveBindingOverrides()
        {
            if (_actionAsset == null) return string.Empty;
            return _actionAsset.SaveBindingOverridesAsJson();
        }

        public void LoadBindingOverrides(string data)
        {
            if (_actionAsset == null || string.IsNullOrEmpty(data)) return;
            _actionAsset.LoadBindingOverridesFromJson(data);
        }

        public void ResetBindingOverrides(string action)
        {
            var inputAction = GetAction(action);
            inputAction?.RemoveAllBindingOverrides();
        }

        public void ResetAllBindingOverrides()
        {
            if (_actionAsset == null) return;
            foreach (var map in _actionAsset.actionMaps)
            {
                map.RemoveAllBindingOverrides();
            }
        }

        #endregion

        #region 运行时重新绑定

        public IRebindingOperation StartRebinding(string action, string bindingId, uint playerId = 0)
        {
            if (string.IsNullOrEmpty(action))
            {
                Debug.LogError("[InputSystemProvider] StartRebinding failed: action is null or empty.");
                return null;
            }

            var inputAction = GetAction(action);
            if (inputAction == null)
            {
                Debug.LogError($"[InputSystemProvider] StartRebinding failed: action '{action}' not found.");
                return null;
            }

            // 根据 bindingId 查找对应的 binding index
            var bindingIndex = FindBindingIndexById(inputAction, bindingId);
            if (bindingIndex < 0)
            {
                // 如果找不到，全新绑定（使用第一个有效的非复合绑定）
                bindingIndex = GetFirstBindableIndex(inputAction);
                if (bindingIndex < 0)
                {
                    Debug.LogError($"[InputSystemProvider] StartRebinding failed: action '{action}' has no bindable binding.");
                    return null;
                }
            }

            var rebindOp = inputAction.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Pointer>/delta")
                .WithControlsExcluding("<Gamepad>/leftStick")
                .OnMatchWaitForAnother(0.1f)
                .Start();

            return new SystemRebindingOperation(rebindOp, inputAction, bindingIndex, bindingId ?? string.Empty);
        }

        /// <summary>
        /// 根据 bindingId 在 Action 的所有绑定中查找索引。
        /// </summary>
        private int FindBindingIndexById(InputAction inputAction, string bindingId)
        {
            if (string.IsNullOrEmpty(bindingId)) return -1;

            var bindings = inputAction.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].id.ToString() == bindingId)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 获取第一个可绑定的普通绑定索引（跳过复合及其子项）。
        /// </summary>
        private int GetFirstBindableIndex(InputAction inputAction)
        {
            var bindings = inputAction.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                return i;
            }
            return -1;
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
                    MessageManager.Publish(new Messages.DeviceConnectedMessage());
                    break;

                case InputDeviceChange.Removed:
                    MessageManager.Publish(new Messages.DeviceDisconnectedMessage());
                    break;

                case InputDeviceChange.Reconnected:
                    MessageManager.Publish(new Messages.DeviceConnectedMessage());
                    break;

                case InputDeviceChange.Disconnected:
                    MessageManager.Publish(new Messages.DeviceDisconnectedMessage());
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
                var previousType = _activeGamepadType;
                _activeGamepadType = currentType;
                MessageManager.Publish(new Messages.GamepadTypeChangedMessage(previousType, currentType));
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