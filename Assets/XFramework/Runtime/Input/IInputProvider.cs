using System;

namespace XFramework.XInput
{
    /// <summary>
    /// 输入提供者接口。所有底层输入实现（Unity Input System、Rewired 等）必须实现此接口。
    /// <para>使用 string 作为动作标识，不与任何游戏类型耦合。第三方项目可在自己的代码中自由定义动作名和输入封装。</para>
    /// <para>支持多玩家（通过 <paramref name="playerId"/> 参数，默认 0）。</para>
    /// </summary>
    public interface IInputProvider : IDisposable
    {
        #region 初始化

        /// <summary>
        /// 初始化输入系统。加载 Input Action Asset 并启用默认 ActionMap。
        /// </summary>
        void Initialize();

        /// <summary>
        /// 指定动作名在当前输入资产中是否存在(任意 ActionMap)。
        /// <para>用于运行时校验动作名拼写;未初始化时返回 false。</para>
        /// </summary>
        /// <param name="action">动作名称,如 "Jump"</param>
        bool HasAction(string action);

        #endregion

        #region 每帧更新

        /// <summary>
        /// 每帧调用一次，刷新内部输入状态。
        /// <para>应在 Unity 的 Update 或类似生命周期中调用。</para>
        /// </summary>
        void Tick();

        #endregion

        #region 按钮事件

        /// <summary>
        /// 本帧是否按下了指定动作。
        /// </summary>
        /// <param name="action">动作名称，如 "Jump"、"Fire"</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        bool WasPressedThisFrame(string action, uint playerId = 0);

        /// <summary>
        /// 本帧是否释放了指定动作。
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        bool WasReleasedThisFrame(string action, uint playerId = 0);

        /// <summary>
        /// 指定动作当前是否被按住。
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        bool IsPressed(string action, uint playerId = 0);

        #endregion

        #region 值输入

        /// <summary>
        /// 读取指定动作的 Vector2 值（如移动摇杆、鼠标增量），已应用绑定处理器（如死区/灵敏度）。
        /// </summary>
        /// <param name="action">动作名称，如 "Move"、"Look"</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        UnityEngine.Vector2 ReadVector2(string action, uint playerId = 0);

        /// <summary>
        /// 读取指定动作的 float 值（如扳机键），已应用绑定处理器（如死区/灵敏度）。
        /// </summary>
        /// <param name="action">动作名称，如 "Throttle"</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        float ReadFloat(string action, uint playerId = 0);

        /// <summary>
        /// 读取指定动作的 float 原始值，未应用绑定处理器（如死区/灵敏度）。
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        float ReadFloatRaw(string action, uint playerId = 0);

        /// <summary>
        /// 读取指定动作的 Vector2 原始值，未应用绑定处理器（如死区/灵敏度）。
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        UnityEngine.Vector2 ReadVector2Raw(string action, uint playerId = 0);

        #endregion

        #region 长按检测

        /// <summary>
        /// 获取指定动作被持续按下的时长（秒）。
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        float GetButtonPressDuration(string action, uint playerId = 0);

        #endregion

        #region 设备管理

        /// <summary>
        /// 当前激活的手柄类型。
        /// </summary>
        GamepadType ActiveGamepadType { get; }

        /// <summary>
        /// 当前正在使用的输入设备类型（键盘鼠标 / 手柄 / 触摸）。
        /// <para>用于 UI 自动切换输入提示图标。</para>
        /// </summary>
        InputDeviceType LastActiveDeviceType { get; }

        #endregion

        #region 振动

        /// <summary>
        /// 设置手柄振动。
        /// </summary>
        /// <param name="playerId">玩家 ID</param>
        /// <param name="leftMotor">左马达强度 [0, 1]</param>
        /// <param name="rightMotor">右马达强度 [0, 1]</param>
        /// <param name="duration">持续时间（秒），0 表示持续振动直到手动停止</param>
        void SetVibration(uint playerId, float leftMotor, float rightMotor, float duration);

        /// <summary>
        /// 停止指定玩家的手柄振动。
        /// </summary>
        /// <param name="playerId">玩家 ID</param>
        void StopVibration(uint playerId);

        /// <summary>
        /// 停止所有玩家的手柄振动。
        /// </summary>
        void StopAllVibration();

        #endregion

        #region ActionMap 管理

        /// <summary>
        /// 切换到指定 ActionMap（如 "Player" / "UI" / "Menu"）。
        /// <para>此方法会先禁用所有已启用的 ActionMap，再启用目标。</para>
        /// <para>如需叠加启用多个 ActionMap，请使用 <see cref="EnableActionMap"/>。</para>
        /// </summary>
        void SwitchActionMap(string mapName);

        /// <summary>
        /// 叠加启用指定 ActionMap（不禁用其他已启用的 ActionMap）。
        /// </summary>
        void EnableActionMap(string mapName);

        /// <summary>
        /// 禁用指定 ActionMap。
        /// </summary>
        void DisableActionMap(string mapName);

        /// <summary>
        /// 禁用所有 ActionMap。
        /// </summary>
        void DisableAllActionMaps();

        #endregion

        #region 绑定查询

        /// <summary>
        /// 获取指定动作当前最佳绑定的人类可读显示名称。
        /// <para>用于 UI 按键提示（如显示 "W"、"X 按钮"）。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        string GetBindingDisplayString(string action, uint playerId = 0);

        /// <summary>
        /// 获取指定动作的所有绑定信息列表。
        /// <para>用于按键设置 UI 展示当前设备下的所有绑定。</para>
        /// </summary>
        /// <param name="action">动作名称</param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        System.Collections.Generic.IReadOnlyList<InputBindingInfo> GetBindings(string action, uint playerId = 0);

        #endregion

        #region 绑定持久化

        /// <summary>
        /// 将所有自定义绑定覆盖序列化为字符串（格式由各 Provider 自行决定）。
        /// <para>Unity Input System 返回 JSON，Rewired 返回 XML。</para>
        /// </summary>
        string SaveBindingOverrides();

        /// <summary>
        /// 从字符串恢复自定义绑定覆盖。
        /// </summary>
        /// <param name="data">由 <see cref="SaveBindingOverrides"/> 生成的字符串</param>
        void LoadBindingOverrides(string data);

        /// <summary>
        /// 重置指定动作的所有绑定覆盖为默认值。
        /// </summary>
        /// <param name="action">动作名称</param>
        void ResetBindingOverrides(string action);

        /// <summary>
        /// 重置所有动作的绑定覆盖为默认值。
        /// </summary>
        void ResetAllBindingOverrides();

        #endregion

        #region 运行时重新绑定

        /// <summary>
        /// 开始交互式按键重绑定。调用后框架等待用户按下物理按键/按钮。
        /// <para>用于按键设置 UI：用户选择一个绑定项，按下新按键完成重绑定。</para>
        /// </summary>
        /// <param name="action">要重新绑定的动作名称</param>
        /// <param name="bindingId">
        /// 要覆盖的绑定唯一标识（对应 <see cref="GetBindings"/> 返回的 <see cref="InputBindingInfo.Id"/>）。
        /// 传入 null 或空字符串表示回退到第一个可绑定索引（覆盖式）；传入的 id 不存在时返回 null 并记录错误日志。
        /// </param>
        /// <param name="playerId">玩家 ID，默认 0</param>
        /// <returns>可取消的绑定操作句柄，绑定完成后通过 <see cref="IRebindingOperation.OnCompleted"/> 事件通知</returns>
        IRebindingOperation StartRebinding(string action, string bindingId, uint playerId = 0);

        #endregion
    }
}
