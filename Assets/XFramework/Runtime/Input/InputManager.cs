using System;
using R3;
using UnityEngine;
using XFramework.XInput.Default;

namespace XFramework.XInput
{
    /// <summary>
    /// 全局输入管理器外观。提供静态方法直接访问输入状态。
    /// <para>内部持有 <see cref="IInputProvider"/> 实例，所有调用委托到该实例。</para>
    /// <para>默认使用 <see cref="InputSystemProvider"/>（基于 Unity Input System），也可通过 <see cref="Initialize(IInputProvider)"/> 注入自定义实现（如 Rewired 适配器）。</para>
    /// <para>使用前需调用 <see cref="Initialize()"/>。每帧需调用 <see cref="Tick()"/>。</para>
    /// <para>框架不定义任何游戏专属动作（如 Jump、Attack），第三方游戏需自行封装。详见 README 中的完整示例。</para>
    /// </summary>
    public static class InputManager
    {
        #region Static — Global Singleton

        private static IInputProvider _provider;
        private static bool _initialized;

        /// <summary>
        /// 全局输入管理器是否已初始化。
        /// </summary>
        public static bool IsInitialized
        {
            get { return _initialized && _provider != null; }
        }

        /// <summary>
        /// 获取底层的 <see cref="IInputProvider"/> 实例。若需要设置协程宿主等扩展配置，可从此处获取。
        /// </summary>
        public static IInputProvider Provider
        {
            get { return _provider; }
        }

        /// <summary>
        /// 使用默认的 <see cref="InputSystemProvider"/> 初始化输入管理器。
        /// <para>基于 Unity Input System，零额外依赖。</para>
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
            {
                UnityEngine.Debug.LogWarning("[InputManager] Initialize was called more than once. Ignoring duplicate.");
                return;
            }

            var provider = new InputSystemProvider();
            provider.Initialize();
            _provider = provider;
            _initialized = true;
        }

        /// <summary>
        /// 使用自定义 <see cref="IInputProvider"/> 初始化输入管理器。
        /// <para>适用于注入 Rewired 适配器或其他自定义实现。</para>
        /// </summary>
        /// <param name="customProvider">自定义输入提供者</param>
        public static void Initialize(IInputProvider customProvider)
        {
            if (_initialized)
            {
                UnityEngine.Debug.LogWarning("[InputManager] Initialize was called more than once. Ignoring duplicate.");
                return;
            }

            _provider = customProvider ?? throw new ArgumentNullException(nameof(customProvider));
            _provider.Initialize();
            _initialized = true;
        }

        /// <summary>
        /// 设置外部已创建的 <see cref="IInputProvider"/> 实例作为全局提供者。
        /// <para>适用于依赖注入或单元测试场景。</para>
        /// </summary>
        public static void SetProvider(IInputProvider provider)
        {
            if (_provider != null)
            {
                _provider.Dispose();
            }
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _initialized = true;
        }

        /// <summary>
        /// 销毁全局输入管理器，释放所有资源。
        /// </summary>
        public static void Destroy()
        {
            if (_provider != null)
            {
                _provider.Dispose();
                _provider = null;
            }
            _initialized = false;
        }

        #endregion

        #region Public API — Tick

        /// <summary>
        /// 每帧调用一次，刷新内部输入状态。应在 Unity 的 Update 或 FixedUpdate 中调用。
        /// </summary>
        public static void Tick()
        {
            _provider?.Tick();
        }

        #endregion

        #region Public API — Button Events

        /// <summary>
        /// 本帧是否按下了指定动作。
        /// </summary>
        /// <param name="action">动作名称，如 "Jump"、"Fire"</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        public static bool WasPressedThisFrame(string action, uint playerId = 0)
        {
            return _provider?.WasPressedThisFrame(action, playerId) ?? false;
        }

        /// <summary>
        /// 本帧是否释放了指定动作。
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        public static bool WasReleasedThisFrame(string action, uint playerId = 0)
        {
            return _provider?.WasReleasedThisFrame(action, playerId) ?? false;
        }

        /// <summary>
        /// 指定动作当前是否被按住。
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        public static bool IsPressed(string action, uint playerId = 0)
        {
            return _provider?.IsPressed(action, playerId) ?? false;
        }

        #endregion

        #region Public API — Value Input

        /// <summary>
        /// 读取指定动作的 Vector2 值（如移动摇杆、鼠标增量），已应用 Input Behavior 的平滑处理。
        /// </summary>
        /// <param name="action">动作名称，如 "Move"、"Look"</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        public static UnityEngine.Vector2 ReadVector2(string action, uint playerId = 0)
        {
            return _provider?.ReadVector2(action, playerId) ?? UnityEngine.Vector2.zero;
        }

        /// <summary>
        /// 读取指定动作的 float 值（如扳机键），已应用平滑处理。
        /// </summary>
        /// <param name="action">动作名称，如 "Throttle"</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        public static float ReadFloat(string action, uint playerId = 0)
        {
            return _provider?.ReadFloat(action, playerId) ?? 0f;
        }

        /// <summary>
        /// 读取指定动作的 float 原始值，不应用平滑处理。
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        public static float ReadFloatRaw(string action, uint playerId = 0)
        {
            return _provider?.ReadFloatRaw(action, playerId) ?? 0f;
        }

        /// <summary>
        /// 读取指定动作的 Vector2 原始值，不应用平滑处理。
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        public static UnityEngine.Vector2 ReadVector2Raw(string action, uint playerId = 0)
        {
            return _provider?.ReadVector2Raw(action, playerId) ?? UnityEngine.Vector2.zero;
        }

        #endregion

        #region Public API — Press Duration

        /// <summary>
        /// 获取指定动作被持续按下的时长（秒）。
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        public static float GetButtonPressDuration(string action, uint playerId = 0)
        {
            return _provider?.GetButtonPressDuration(action, playerId) ?? 0f;
        }

        #endregion

        #region Public API — Device

        /// <summary>
        /// 当前激活的手柄类型。
        /// </summary>
        public static GamepadType ActiveGamepadType
        {
            get
            {
                if (_provider != null)
                    return _provider.ActiveGamepadType;
                return GamepadType.None;
            }
        }

        /// <summary>
        /// 当前正在使用的输入设备类型（键盘鼠标 / 手柄 / 触摸）。
        /// <para>用于 UI 自动切换输入提示图标。</para>
        /// </summary>
        public static InputDeviceType LastActiveDeviceType
        {
            get
            {
                if (_provider != null)
                    return _provider.LastActiveDeviceType;
                return InputDeviceType.None;
            }
        }

        #endregion

        #region Public API — Vibration

        /// <summary>
        /// 设置手柄振动。
        /// </summary>
        /// <param name="playerId">玩家 ID</param>
        /// <param name="leftMotor">左马达强度 [0, 1]</param>
        /// <param name="rightMotor">右马达强度 [0, 1]</param>
        /// <param name="duration">持续时间（秒），0 表示持续振动直到手动停止</param>
        public static void SetVibration(uint playerId, float leftMotor, float rightMotor, float duration)
        {
            _provider?.SetVibration(playerId, leftMotor, rightMotor, duration);
        }

        /// <summary>
        /// 停止指定玩家的手柄振动。
        /// </summary>
        public static void StopVibration(uint playerId)
        {
            _provider?.StopVibration(playerId);
        }

        /// <summary>
        /// 停止所有玩家的手柄振动。
        /// </summary>
        public static void StopAllVibration()
        {
            _provider?.StopAllVibration();
        }

        #endregion

        #region Public API — ActionMap

        /// <summary>
        /// 切换到指定 ActionMap（如 "Player" / "UI" / "Menu"）。
        /// <para>此方法会先禁用所有已启用的 ActionMap，再启用目标。</para>
        /// </summary>
        /// <param name="mapName">ActionMap 名称</param>
        public static void SwitchActionMap(string mapName)
        {
            _provider?.SwitchActionMap(mapName);
        }

        /// <summary>
        /// 叠加启用指定 ActionMap（不禁用其他已启用的 ActionMap）。
        /// </summary>
        /// <param name="mapName">ActionMap 名称</param>
        public static void EnableActionMap(string mapName)
        {
            _provider?.EnableActionMap(mapName);
        }

        /// <summary>
        /// 禁用指定 ActionMap。
        /// </summary>
        /// <param name="mapName">ActionMap 名称</param>
        public static void DisableActionMap(string mapName)
        {
            _provider?.DisableActionMap(mapName);
        }

        /// <summary>
        /// 禁用所有 ActionMap。
        /// </summary>
        public static void DisableAllActionMaps()
        {
            _provider?.DisableAllActionMaps();
        }

        #endregion

        #region Public API — Binding

        /// <summary>
        /// 获取指定动作当前最佳绑定的人类可读显示名称。
        /// <para>根据当前活跃设备类型自动选择对应的按键提示（如键盘显示 "W"，手柄显示 "X 按钮"）。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        public static string GetBindingDisplayString(string action, uint playerId = 0)
        {
            return _provider?.GetBindingDisplayString(action, playerId) ?? string.Empty;
        }

        /// <summary>
        /// 获取指定动作的所有绑定信息列表。
        /// <para>用于按键设置 UI 展示当前设备下的所有绑定。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        public static System.Collections.Generic.IReadOnlyList<InputBindingInfo> GetBindings(string action, uint playerId = 0)
        {
            return _provider?.GetBindings(action, playerId);
        }

        #endregion

        #region Public API — Binding Override Persistence

        /// <summary>
        /// 将所有自定义绑定覆盖序列化为字符串。
        /// <para>Unity Input System 返回 JSON，Rewired 适配器返回 XML。</para>
        /// <para>调用方应自行将返回的字符串写入 PlayerPrefs 或文件。</para>
        /// </summary>
        public static string SaveBindingOverrides()
        {
            return _provider?.SaveBindingOverrides() ?? string.Empty;
        }

        /// <summary>
        /// 从字符串恢复自定义绑定覆盖。
        /// </summary>
        /// <param name="data">由 <see cref="SaveBindingOverrides"/> 生成的字符串</param>
        public static void LoadBindingOverrides(string data)
        {
            _provider?.LoadBindingOverrides(data);
        }

        /// <summary>
        /// 重置指定动作的所有绑定覆盖为默认值。
        /// </summary>
        /// <param name="action">动作名称</param>
        public static void ResetBindingOverrides(string action)
        {
            _provider?.ResetBindingOverrides(action);
        }

        /// <summary>
        /// 重置所有动作的绑定覆盖为默认值。
        /// </summary>
        public static void ResetAllBindingOverrides()
        {
            _provider?.ResetAllBindingOverrides();
        }

        #endregion

        #region Public API — Interactive Rebinding

        /// <summary>
        /// 开始交互式按键重绑定。调用后框架等待用户按下物理按键/按钮。
        /// <para>用于按键设置 UI：用户选择一个绑定项，按下新按键完成重绑定。</para>
        /// </summary>
        /// <param name="action">要重新绑定的动作名称</param>
        /// <param name="bindingId">
        /// 要覆盖的绑定唯一标识（对应 <see cref="GetBindings"/> 返回的 <see cref="InputBindingInfo.Id"/>）。
        /// 传入 null 或空字符串表示新增一条绑定。
        /// </param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        /// <returns>可取消的绑定操作句柄，绑定完成后通过 <see cref="IRebindingOperation.OnCompleted"/> 事件通知</returns>
        public static IRebindingOperation StartRebinding(string action, string bindingId, uint playerId = 0)
        {
            return _provider?.StartRebinding(action, bindingId, playerId);
        }

        #endregion

        #region Reactive Input

        /// <summary>
        /// 订阅按钮按下事件。每帧检测 <see cref="WasPressedThisFrame"/>，触发时回调一次。
        /// <para>传入 <paramref name="context"/> 可自动随组件销毁取消订阅，无需手动 Dispose。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="callback">按下时回调</param>
        /// <param name="context">生命周期绑定的组件（可选），传入后可自动取消订阅</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable ObservePressed(string action, Action callback, MonoBehaviour context = null, uint playerId = 0)
        {
            var sub = Observable.EveryUpdate()
                .Where(_ => WasPressedThisFrame(action, playerId))
                .Subscribe(_ => callback());

            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());

            return sub;
        }

        /// <summary>
        /// 订阅按钮释放事件。每帧检测 <see cref="WasReleasedThisFrame"/>，触发时回调一次。
        /// <para>传入 <paramref name="context"/> 可自动随组件销毁取消订阅，无需手动 Dispose。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="callback">释放时回调</param>
        /// <param name="context">生命周期绑定的组件（可选），传入后可自动取消订阅</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable ObserveReleased(string action, Action callback, MonoBehaviour context = null, uint playerId = 0)
        {
            var sub = Observable.EveryUpdate()
                .Where(_ => WasReleasedThisFrame(action, playerId))
                .Subscribe(_ => callback());

            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());

            return sub;
        }

        /// <summary>
        /// 订阅按钮按住状态。每帧读取 <see cref="IsPressed"/> 的值，仅当状态发生变化时回调。
        /// <para>传入 <paramref name="context"/> 可自动随组件销毁取消订阅，无需手动 Dispose。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="callback">状态变化时回调，参数为当前是否按住</param>
        /// <param name="context">生命周期绑定的组件（可选），传入后可自动取消订阅</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable ObserveHeld(string action, Action<bool> callback, MonoBehaviour context = null, uint playerId = 0)
        {
            var sub = Observable.EveryUpdate()
                .Select(_ => IsPressed(action, playerId))
                .DistinctUntilChanged()
                .Subscribe(callback);

            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());

            return sub;
        }

        /// <summary>
        /// 订阅按钮持续按下时长（秒）。每帧回调当前按住时长。
        /// <para>使用 <see cref="DistinctUntilChanged"/> 去重，仅值变化时触发回调。</para>
        /// <para>传入 <paramref name="context"/> 可自动随组件销毁取消订阅，无需手动 Dispose。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="callback">每帧回调当前按住时长（秒）</param>
        /// <param name="context">生命周期绑定的组件（可选），传入后可自动取消订阅</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable ObservePressDuration(string action, Action<float> callback, MonoBehaviour context = null, uint playerId = 0)
        {
            var sub = Observable.EveryUpdate()
                .Select(_ => GetButtonPressDuration(action, playerId))
                .DistinctUntilChanged()
                .Subscribe(callback);

            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());

            return sub;
        }

        /// <summary>
        /// 订阅 Vector2 轴输入（如移动摇杆）。使用 <see cref="ReadVector2"/> 读取平滑值，
        /// 仅当值变化时回调。
        /// <para>传入 <paramref name="context"/> 可自动随组件销毁取消订阅，无需手动 Dispose。</para>
        /// </summary>
        /// <param name="action">动作名称，如 "Move"</param>
        /// <param name="callback">值变化时回调</param>
        /// <param name="context">生命周期绑定的组件（可选），传入后可自动取消订阅</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable ObserveVector2(string action, Action<Vector2> callback, MonoBehaviour context = null, uint playerId = 0)
        {
            var sub = Observable.EveryUpdate()
                .Select(_ => ReadVector2(action, playerId))
                .DistinctUntilChanged()
                .Subscribe(callback);

            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());

            return sub;
        }

        /// <summary>
        /// 订阅 float 轴输入。使用 <see cref="ReadFloat"/> 读取平滑值，仅当值变化时回调。
        /// <para>传入 <paramref name="context"/> 可自动随组件销毁取消订阅，无需手动 Dispose。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="callback">值变化时回调</param>
        /// <param name="context">生命周期绑定的组件（可选），传入后可自动取消订阅</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable ObserveFloat(string action, Action<float> callback, MonoBehaviour context = null, uint playerId = 0)
        {
            var sub = Observable.EveryUpdate()
                .Select(_ => ReadFloat(action, playerId))
                .DistinctUntilChanged()
                .Subscribe(callback);

            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());

            return sub;
        }

        /// <summary>
        /// 订阅 Vector2 原始轴输入（不平滑）。使用 <see cref="ReadVector2Raw"/>，仅当值变化时回调。
        /// <para>传入 <paramref name="context"/> 可自动随组件销毁取消订阅，无需手动 Dispose。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="callback">值变化时回调</param>
        /// <param name="context">生命周期绑定的组件（可选），传入后可自动取消订阅</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable ObserveVector2Raw(string action, Action<Vector2> callback, MonoBehaviour context = null, uint playerId = 0)
        {
            var sub = Observable.EveryUpdate()
                .Select(_ => ReadVector2Raw(action, playerId))
                .DistinctUntilChanged()
                .Subscribe(callback);

            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());

            return sub;
        }

        /// <summary>
        /// 订阅 float 原始轴输入（不平滑）。使用 <see cref="ReadFloatRaw"/>，仅当值变化时回调。
        /// <para>传入 <paramref name="context"/> 可自动随组件销毁取消订阅，无需手动 Dispose。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="callback">值变化时回调</param>
        /// <param name="context">生命周期绑定的组件（可选），传入后可自动取消订阅</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        /// <returns>可手动取消订阅的句柄</returns>
        public static IDisposable ObserveFloatRaw(string action, Action<float> callback, MonoBehaviour context = null, uint playerId = 0)
        {
            var sub = Observable.EveryUpdate()
                .Select(_ => ReadFloatRaw(action, playerId))
                .DistinctUntilChanged()
                .Subscribe(callback);

            if (context != null)
                context.destroyCancellationToken.Register(() => sub.Dispose());

            return sub;
        }

        #endregion
    }
}
