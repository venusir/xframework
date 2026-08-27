using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
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
    internal sealed class InputSystemProvider : IInputProvider
    {
        #region Private Fields

        private InputActionAsset _actionAsset;
        private string _currentMapName;
        private GamepadType _activeGamepadType;
        private InputDeviceType _lastActiveDeviceType;

        // 通用动作字典缓存：按需懒加载 Action 引用，避免每帧全量字符串查找
        private readonly Dictionary<string, InputAction> _actionCache = new Dictionary<string, InputAction>(32);

        #if UNITY_EDITOR
        // 已警告未命中的动作名(Editor 专用):GetAction 在每帧读取路径被调用,同一动作只警告一次防止刷屏
        private readonly HashSet<string> _warnedActionNames = new HashSet<string>();
        #endif

        // 多玩家:action 名 → 该 action 第一个 Gamepad 绑定的控件路径尾(如 "buttonSouth"、"leftStick");
        // 空串表示无 Gamepad 绑定(纯键鼠动作),避免每帧重复遍历绑定解析
        private readonly Dictionary<string, string> _playerBindingPaths = new Dictionary<string, string>(8);

        // 振动到期时刻:玩家 ID → Time.unscaledTime 绝对时刻;由 Tick 帧驱动到期自动停止,不使用协程
        private readonly Dictionary<uint, float> _vibrationUntilTimes = new Dictionary<uint, float>(4);

        // 振动到期临时收集(复用,避免每帧分配)
        private readonly List<uint> _expiredVibrationPlayers = new List<uint>(4);

        // 长按计时：记录每个动作首次按下时间（按需增长，支持任意动作名）
        private readonly Dictionary<string, float> _buttonPressStartTimes = new Dictionary<string, float>(32);

        // GamepadType 检测缓存:按设备 ID 记忆最近识别结果,避免每帧解析 capabilities JSON(有分配)
        private int _detectedGamepadDeviceId;
        private GamepadType _detectedGamepadType;

        #endregion

        #region Properties

        public GamepadType ActiveGamepadType => _activeGamepadType;
        public InputDeviceType LastActiveDeviceType => _lastActiveDeviceType;

        #endregion

        #region Initialize

        public void Initialize()
        {
            // 加载 Input Action Asset(资源须位于 Assets/Resources/)
            var asset = Resources.Load<InputActionAsset>("InputSystem_Actions");

            if (asset == null)
            {
                // 显式抛异常而非静默返回:加载失败后所有读取会静默返回默认值,掩盖配置错误
                throw new InvalidOperationException(
                    "[Input] 加载 InputSystem_Actions.inputactions 失败:请将 InputSystem_Actions.inputactions 放入 Assets/Resources/ 目录后重试");
            }

            Initialize(asset);
        }

        /// <summary>
        /// 以指定资产初始化。
        /// <para>测试经 InternalsVisibleTo 注入程序化资产;亦可作为资产自管加载(如经 YooAsset)时的替代入口。</para>
        /// </summary>
        internal void Initialize(InputActionAsset asset)
        {
            _actionAsset = asset ?? throw new ArgumentNullException(nameof(asset));
            _actionAsset.Enable();

            // 监听设备变更
            InputSystem.onDeviceChange += OnInputDeviceChange;

            // 默认启用 Player map
            SwitchActionMap("Player");

            Debug.Log("[Input] Initialized successfully.");
        }

        #endregion

        #region Tick

        public void Tick()
        {
            // 振动到期检查置于资产空检查之前:资产未加载时也要保证马达能停
            CheckVibrationTimeout();

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
            if (inputAction == null) return false;

            // 0 号玩家沿用 action 级读取(全设备);playerId > 0 直读玩家手柄控件,键鼠不服务非 0 号玩家
            if (playerId == 0) return inputAction.WasPressedThisFrame();

            var control = GetPlayerControl(inputAction, playerId);
            return control != null && WasPressedOnControl(control);
        }

        public bool WasReleasedThisFrame(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            if (inputAction == null) return false;

            if (playerId == 0) return inputAction.WasReleasedThisFrame();

            var control = GetPlayerControl(inputAction, playerId);
            return control != null && WasReleasedOnControl(control);
        }

        public bool IsPressed(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            if (inputAction == null) return false;

            if (playerId == 0) return inputAction.IsPressed();

            var control = GetPlayerControl(inputAction, playerId);
            return control != null && IsPressedOnControl(control);
        }

        #endregion

        #region Value Input

        public Vector2 ReadVector2(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            if (inputAction == null) return Vector2.zero;

            // 0 号玩家沿用 action 级读取(应用绑定处理器);playerId > 0 读玩家手柄控件
            if (playerId == 0) return inputAction.ReadValue<Vector2>();

            var control = GetPlayerControl(inputAction, playerId);
            return control is InputControl<Vector2> typedControl ? typedControl.ReadValue() : Vector2.zero;
        }

        public float ReadFloat(string action, uint playerId = 0)
        {
            var inputAction = GetAction(action);
            if (inputAction == null) return 0f;

            if (playerId == 0) return inputAction.ReadValue<float>();

            var control = GetPlayerControl(inputAction, playerId);
            return control is InputControl<float> typedControl ? typedControl.ReadValue() : 0f;
        }

        public float ReadFloatRaw(string action, uint playerId = 0)
        {
            // 0 号玩家直读当前驱动控件值,绕过绑定处理器(如死区/灵敏度),返回设备原始输入
            var inputAction = GetAction(action);
            if (inputAction == null) return 0f;

            if (playerId == 0) return ReadRawValue<float>(inputAction.activeControl, 0f);

            // playerId > 0:控件级读取本身即原始值(绑定处理器在 action 解析层生效),与平滑版返回相同
            var control = GetPlayerControl(inputAction, playerId);
            return control is InputControl<float> typedControl ? typedControl.ReadValue() : 0f;
        }

        public Vector2 ReadVector2Raw(string action, uint playerId = 0)
        {
            // 0 号玩家直读当前驱动控件值,绕过绑定处理器(如死区/灵敏度),返回设备原始输入
            var inputAction = GetAction(action);
            if (inputAction == null) return Vector2.zero;

            if (playerId == 0) return ReadRawValue<Vector2>(inputAction.activeControl, Vector2.zero);

            // playerId > 0:控件级读取本身即原始值(绑定处理器在 action 解析层生效),与平滑版返回相同
            var control = GetPlayerControl(inputAction, playerId);
            return control is InputControl<Vector2> typedControl ? typedControl.ReadValue() : Vector2.zero;
        }

        /// <summary>
        /// 读取控件原始值。
        /// <para><see cref="InputControl"/> 基类不暴露泛型 <c>ReadValue</c>,需按具体值类型转型;
        /// 控件不存在或值类型不匹配时返回 fallback(零 GC,无异常)。</para>
        /// </summary>
        private static TValue ReadRawValue<TValue>(InputControl control, TValue fallback)
            where TValue : struct
        {
            return control is InputControl<TValue> typedControl ? typedControl.ReadValue() : fallback;
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
                // 仅在状态实际变化时启用并记录,避免高频切换产生无意义的日志与字符串分配
                if (targetMap.enabled) return;

                targetMap.Enable();
                _currentMapName = mapName;
                InvalidateCaches();

                Debug.Log($"[Input] Enabled action map: {mapName}");
            }
            else
            {
                Debug.LogWarning($"[Input] ActionMap '{mapName}' not found.");
            }
        }

        public void DisableActionMap(string mapName)
        {
            if (_actionAsset == null) return;

            var targetMap = _actionAsset.FindActionMap(mapName);
            if (targetMap != null)
            {
                // 仅在状态实际变化时禁用并记录,避免高频切换产生无意义的日志与字符串分配
                if (!targetMap.enabled) return;

                targetMap.Disable();
                InvalidateCaches();

                Debug.Log($"[Input] Disabled action map: {mapName}");
            }
        }

        public void DisableAllActionMaps()
        {
            if (_actionAsset == null) return;

            var anyDisabled = false;
            foreach (var map in _actionAsset.actionMaps)
            {
                if (!map.enabled) continue;
                map.Disable();
                anyDisabled = true;
            }

            // 仅在确有 map 被禁用时记录,避免每帧重复调用产生无意义的日志与字符串分配
            if (anyDisabled)
            {
                InvalidateCaches();
                Debug.Log("[Input] All action maps disabled.");
            }
        }

        /// <summary>
        /// 失效 Action 缓存与长按计时。
        /// <para>切换/启停 ActionMap 后,同名 Action 可能属于不同 Map,缓存按 action 名 key 会串 Map;
        /// 失效后下次访问按「先查已启用 Map」重新解析(见 <see cref="GetAction"/>)。</para>
        /// </summary>
        private void InvalidateCaches()
        {
            _actionCache.Clear();
            _buttonPressStartTimes.Clear();
            _playerBindingPaths.Clear();
        }

        #endregion

        #region Vibration

        public void SetVibration(uint playerId, float leftMotor, float rightMotor, float duration)
        {
            var gamepad = GetGamepadForPlayer(playerId);
            if (gamepad == null) return;

            // 限制范围 [0, 1]
            leftMotor = Mathf.Clamp01(leftMotor);
            rightMotor = Mathf.Clamp01(rightMotor);

            gamepad.SetMotorSpeeds(leftMotor, rightMotor);

            // 指定了持续时间则记录该玩家到期时刻,由 Tick 帧驱动到期自动停止(duration=0 表示持续振动直到手动停止)
            if (duration > 0f)
            {
                _vibrationUntilTimes[playerId] = Time.unscaledTime + duration;
            }
            else
            {
                _vibrationUntilTimes.Remove(playerId);
            }
        }

        public void StopVibration(uint playerId)
        {
            // 无论当前是否有手柄,先清空到期标记,保证手动停止语义完整
            _vibrationUntilTimes.Remove(playerId);

            var gamepad = GetGamepadForPlayer(playerId);
            if (gamepad == null) return;

            gamepad.SetMotorSpeeds(0f, 0f);
        }

        public void StopAllVibration()
        {
            // 停止所有已连接手柄的马达
            _vibrationUntilTimes.Clear();
            foreach (var gamepad in Gamepad.all)
            {
                gamepad.SetMotorSpeeds(0f, 0f);
            }
        }

        /// <summary>
        /// 帧驱动检查各玩家振动是否到期,到期则停止对应马达。
        /// <para>替代原协程方案(依赖外部注入协程宿主且违反禁用 IEnumerator 约定),零分配、零外部依赖。</para>
        /// </summary>
        private void CheckVibrationTimeout()
        {
            if (_vibrationUntilTimes.Count == 0) return;

            // 先收集到期玩家再逐个停止,避免遍历中修改字典
            _expiredVibrationPlayers.Clear();
            var now = Time.unscaledTime;
            foreach (var kvp in _vibrationUntilTimes)
            {
                if (now >= kvp.Value)
                {
                    _expiredVibrationPlayers.Add(kvp.Key);
                }
            }

            foreach (var playerId in _expiredVibrationPlayers)
            {
                _vibrationUntilTimes.Remove(playerId);

                // 即使该玩家手柄已断开也移除到期标记,避免残留状态在新设备上误触发
                var gamepad = GetGamepadForPlayer(playerId);
                if (gamepad != null)
                {
                    gamepad.SetMotorSpeeds(0f, 0f);
                }
            }
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
            // 第一遍计数(排除复合结构本身及其子项),精确分配数组,避免中间 List 分配
            var count = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                count++;
            }

            var result = new InputBindingInfo[count];
            var fill = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;

                result[fill++] = new InputBindingInfo
                {
                    Id = b.id.ToString(),
                    DisplayName = inputAction.GetBindingDisplayString(i) ?? string.Empty,
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
                Debug.LogError("[Input] StartRebinding failed: action is null or empty.");
                return null;
            }

            var inputAction = GetAction(action);
            if (inputAction == null)
            {
                Debug.LogError($"[Input] StartRebinding failed: action '{action}' not found.");
                return null;
            }

            // 定位要覆盖的绑定:bindingId 非空时精确匹配;为空时回退到第一个可绑定索引(覆盖式)
            int bindingIndex;
            if (!string.IsNullOrEmpty(bindingId))
            {
                bindingIndex = FindBindingIndexById(inputAction, bindingId);
                if (bindingIndex < 0)
                {
                    // 非法 bindingId 显式报错返回,不再静默回退覆盖第一个绑定(避免持久化数据与资产失配时改错键位)
                    Debug.LogError($"[Input] StartRebinding failed: action '{action}' has no binding with id '{bindingId}'.");
                    return null;
                }
            }
            else
            {
                bindingIndex = GetFirstBindableIndex(inputAction);
                if (bindingIndex < 0)
                {
                    Debug.LogError($"[Input] StartRebinding failed: action '{action}' has no bindable binding.");
                    return null;
                }
            }

            // 重绑定要求 action 处于禁用态(InputActionRebindingExtensions.WithAction 对启用态 action 直接抛异常);
            // 游戏内重绑定 UI 打开时 action map 通常是启用的,这里临时禁用、操作结束后经 SystemRebindingOperation 恢复
            var wasEnabled = inputAction.enabled;
            if (wasEnabled) inputAction.Disable();

            InputActionRebindingExtensions.RebindingOperation rebindOp;
            try
            {
                rebindOp = inputAction.PerformInteractiveRebinding(bindingIndex)
                    .WithControlsExcluding("<Mouse>/position")
                    .WithControlsExcluding("<Mouse>/delta")
                    .WithControlsExcluding("<Pointer>/position")
                    .WithControlsExcluding("<Pointer>/delta")
                    .WithControlsExcluding("<Gamepad>/leftStick")
                    .OnMatchWaitForAnother(0.1f)
                    .Start();
            }
            catch
            {
                // 防御:创建失败(如已有重绑定在进行中)时恢复禁用前的状态,避免把游戏输入打坏
                if (wasEnabled) inputAction.Enable();
                throw;
            }

            return new SystemRebindingOperation(rebindOp, inputAction, bindingIndex, bindingId ?? string.Empty, wasEnabled);
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
            _playerBindingPaths.Clear();

            Debug.Log("[Input] Disposed.");
        }

        #endregion

        #region Private — Device Management

        private void OnInputDeviceChange(InputDevice device, InputDeviceChange change)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Reconnected:
                    MessageManager.Publish(new Messages.DeviceConnectedMessage(device.displayName, device.deviceId, device is Gamepad));
                    break;

                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                    MessageManager.Publish(new Messages.DeviceDisconnectedMessage(device.displayName, device.deviceId, device is Gamepad));
                    break;
            }
        }

        private void DetectGamepadType()
        {
            GamepadType currentType;

            var gamepad = Gamepad.current;
            if (gamepad == null)
            {
                currentType = GamepadType.None;
            }
            else if (gamepad.deviceId != _detectedGamepadDeviceId)
            {
                // 设备变化才重新识别(matcher 解析 capabilities JSON 有分配,按设备缓存避免进每帧路径)
                _detectedGamepadDeviceId = gamepad.deviceId;
                _detectedGamepadType = DetectGamepadTypeFrom(gamepad.description);
                currentType = _detectedGamepadType;
            }
            else
            {
                currentType = _detectedGamepadType;
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

        // vendorId/productId 查表:官方 InputDeviceMatcher 匹配 capabilities JSON。
        // 注意:包内 JsonParser 不支持 0x 十六进制前缀(真实 HID 描述符经 JsonUtility 序列化为十进制,无碍)。
        // MatchPercentage 语义:任一 pattern 不匹配即 0,全匹配才 > 0。
        private static readonly InputDeviceMatcher s_GamepadTypeMatcherXbox =
            new InputDeviceMatcher().WithCapability("vendorId", 0x045E); // Microsoft
        private static readonly InputDeviceMatcher s_GamepadTypeMatcherPlayStation4 =
            new InputDeviceMatcher().WithCapability("vendorId", 0x054C).WithCapability("productId", 0x05C4); // Sony DualShock 4
        private static readonly InputDeviceMatcher s_GamepadTypeMatcherPlayStation5 =
            new InputDeviceMatcher().WithCapability("vendorId", 0x054C).WithCapability("productId", 0x0CE6); // Sony DualSense
        private static readonly InputDeviceMatcher s_GamepadTypeMatcherSwitchPro =
            new InputDeviceMatcher().WithCapability("vendorId", 0x057E).WithCapability("productId", 0x2009); // Nintendo Switch Pro Controller

        /// <summary>
        /// 根据设备描述识别手柄类型(纯函数,便于单元测试)。
        /// <para>优先按 vendorId/productId 查表,不受显示名本地化影响;
        /// 查不到(部分蓝牙设备不上报 ID)回退 <see cref="InputDeviceDescription.product"/> 关键词匹配。
        /// 移除原「displayName 长度小于 20」启发式——蓝牙 DualShock 4 与杂牌无线手柄同名,按长度区分不可靠。</para>
        /// </summary>
        internal static GamepadType DetectGamepadTypeFrom(InputDeviceDescription description)
        {
            // vendorId/productId 精确查表
            if (s_GamepadTypeMatcherXbox.MatchPercentage(description) > 0f) return GamepadType.Xbox;
            if (s_GamepadTypeMatcherPlayStation4.MatchPercentage(description) > 0f) return GamepadType.PlayStation4;
            if (s_GamepadTypeMatcherPlayStation5.MatchPercentage(description) > 0f) return GamepadType.PlayStation5;
            if (s_GamepadTypeMatcherSwitchPro.MatchPercentage(description) > 0f) return GamepadType.SwitchPro;

            // 回退:产品名关键词匹配(displayName 由 product 派生且可能本地化,故匹配用 product)
            var name = description.product ?? string.Empty;
            if (name.Contains("DualSense", StringComparison.OrdinalIgnoreCase)) return GamepadType.PlayStation5;
            if (name.Contains("DualShock", StringComparison.OrdinalIgnoreCase)) return GamepadType.PlayStation4;
            if (name.Contains("Switch", StringComparison.OrdinalIgnoreCase) || name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase)) return GamepadType.SwitchPro;
            if (name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) || name.Contains("XInput", StringComparison.OrdinalIgnoreCase)) return GamepadType.Xbox;

            return GamepadType.Generic;
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
            #if UNITY_EDITOR
            else if (_warnedActionNames.Add(actionName))
            {
                Debug.LogWarning($"[Input] action '{actionName}' 不存在于任何 ActionMap,请检查动作名拼写或资产配置(可用 InputManager.HasAction 校验)。");
            }
            #endif
            return fallback;
        }

        /// <summary>
        /// 指定动作名在当前输入资产中是否存在(任意 ActionMap)。
        /// </summary>
        public bool HasAction(string action)
        {
            return _actionAsset != null && _actionAsset.FindAction(action) != null;
        }

        #endregion

        #region Private — Multi-Player

        /// <summary>
        /// 按 playerId 取玩家手柄(最小档:按连接顺序分配)。
        /// <para>0 号玩家沿用 <see cref="Gamepad.current"/> 语义(当前活跃手柄);
        /// playerId &gt; 0 跳过 0 号玩家的手柄后按 <see cref="Gamepad.all"/> 连接顺序分配,
        /// 保证各玩家设备互斥(若直接按 all[playerId] 索引,current 恰好是第二只手柄时
        /// 玩家 0 与玩家 1 会指向同一设备,且第一只手柄永远不可达)。越界或未连接返回 null。</para>
        /// </summary>
        private static Gamepad GetGamepadForPlayer(uint playerId)
        {
            if (playerId == 0) return Gamepad.current;

            var all = Gamepad.all;
            var current = Gamepad.current;
            uint skip = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == current) continue;
                skip++;
                if (skip == playerId) return all[i];
            }
            return null;
        }

        /// <summary>
        /// 取指定玩家手柄上该 action 的驱动控件。
        /// <para>playerId 为 0、玩家无手柄、或动作无 Gamepad 绑定时返回 null。
        /// 键鼠绑定不参与 playerId &gt; 0 的读取(键鼠仅服务 0 号玩家)。</para>
        /// </summary>
        private InputControl GetPlayerControl(InputAction inputAction, uint playerId)
        {
            if (playerId == 0) return null;

            var gamepad = GetGamepadForPlayer(playerId);
            if (gamepad == null) return null;

            var path = GetPlayerGamepadControlPath(inputAction);
            if (path.Length == 0) return null;

            // GetChildControl 按相对路径查控件,零分配;手柄断开后控件失效返回 null,重连自动恢复
            return gamepad.GetChildControl(path);
        }

        /// <summary>
        /// 控件级按下事件判断:读 <see cref="ButtonControl"/> 状态位(本帧按下)。
        /// <para>非按钮控件(如摇杆)无按下事件,返回 false;Input System 1.19 未提供 InputControl 级 WasPressed 扩展。</para>
        /// </summary>
        private static bool WasPressedOnControl(InputControl control)
            => control is ButtonControl button && button.wasPressedThisFrame;

        /// <summary>
        /// 控件级释放事件判断:读 <see cref="ButtonControl"/> 状态位(本帧释放)。
        /// </summary>
        private static bool WasReleasedOnControl(InputControl control)
            => control is ButtonControl button && button.wasReleasedThisFrame;

        /// <summary>
        /// 控件级按住判断:复用官方 <c>IsPressed(InputControl)</c> 扩展(按钮取 pressPointOrDefault,非按钮取全局默认按压点)。
        /// <para>与 action 级 <see cref="InputAction.IsPressed"/> 语义一致,但作用于指定玩家的控件。</para>
        /// </summary>
        private static bool IsPressedOnControl(InputControl control)
            => control.IsPressed();

        /// <summary>
        /// 取 action 第一个 Gamepad 绑定的控件路径尾(如 "buttonSouth"、"leftStick"),结果缓存。
        /// <para>复合绑定取子项路径(如摇杆复合的 leftStick 部件);纯键鼠动作缓存空串,避免每帧重复遍历。</para>
        /// </summary>
        private string GetPlayerGamepadControlPath(InputAction inputAction)
        {
            if (_playerBindingPaths.TryGetValue(inputAction.name, out var cached)) return cached;

            var bindings = inputAction.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite) continue; // 复合根无路径,其子项才是具体设备绑定

                var bindingPath = b.path;
                if (!string.IsNullOrEmpty(bindingPath) && bindingPath.StartsWith("<Gamepad>/", StringComparison.Ordinal))
                {
                    var tail = bindingPath.Substring("<Gamepad>/".Length);
                    _playerBindingPaths[inputAction.name] = tail;
                    return tail;
                }
            }

            _playerBindingPaths[inputAction.name] = string.Empty;
            return string.Empty;
        }

        #endregion
    }
}