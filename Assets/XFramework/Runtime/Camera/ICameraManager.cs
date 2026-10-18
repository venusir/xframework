using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机管理后端契约：命名相机与动态相机句柄的激活 / 反激活 / 过渡与查询。
    /// <para><b>命名 / 动态双键</b>：常驻相机用 string id（构造 map 或具体实现的
    /// <c>Register(string, T)</c>）；随预制创建 / 销毁的动态相机用 <see cref="CameraHandle"/>
    /// （<see cref="Register{T}(T)"/> 返回句柄，之后按句柄操作）。动态相机活动时
    /// <see cref="CameraHandle.NamedId"/> 为 <c>null</c>（门面便利 <c>CameraManager.ActiveCameraId</c>
    /// 直接读它）；按句柄操作用 <see cref="IsActive(CameraHandle)"/>。</para>
    /// <para><b>过场边界</b>：随 CG 预制创建、由 Timeline / Cinemachine 接管 Brain 的过场相机
    /// <b>不经过本接口</b>——Timeline 期间调用激活 / 过渡会与过场冲突；这类相机由过场系统管理。</para>
    /// <para><b>线程</b>：全部成员仅在主线程调用。</para>
    /// <para><b>未知 id / 无效句柄</b>：实现应告警并 no-op，<b>不得抛异常</b>；<c>null</c> id 抛
    /// <see cref="System.ArgumentNullException"/>。无效句柄包括 <c>default(CameraHandle)</c>、
    /// 已注销的旧句柄与代际不符的句柄。</para>
    /// <para><b>过渡语义</b>：<see cref="ActiveHandle"/>（及其 <see cref="CameraHandle.NamedId"/>）在过渡期间
    /// 返回<b>目标</b>；<see cref="IsTransitioning"/> 表示是否有过渡在途。过渡中断、回退与曲线细节由后端定义，
    /// 使用方以这些查询为可移植信号。</para>
    /// </summary>
    public interface ICameraManager
    {
        /// <summary>当前输出相机；没有活动相机时为 <c>null</c>。</summary>
        /// <remarks>过渡期间返回目标的输出相机。使用方可直接用它做世界↔屏幕、射线、FOV 等
        /// <see cref="Camera"/> 操作——本模块刻意不重复封装这些成员。
        /// <para><b>不要用 <c>OutputCamera != null</c> 判断「是否有已注册的活动相机」</b>：Cinemachine 适配器
        /// 即使活动 vcam 未注册也会返回 Brain 的输出相机（恒非空）；要判断活动状态请用
        /// <see cref="ActiveHandle"/> / <see cref="IsActive(string)"/>。核心接口只承诺「没有输出相机时可为 null」，
        /// 不承诺「有输出相机就一定有已注册的活动相机」（CAM-005）。</para></remarks>
        Camera OutputCamera { get; }

        /// <summary>已注册的<b>命名</b>相机 id（主线程实时视图）。动态句柄不在其中。</summary>
        /// <remarks>枚举期间不要注册 / 注销；实现对返回集合的跨线程一致性不作承诺。</remarks>
        IReadOnlyCollection<string> CameraIds { get; }

        /// <summary>当前活动相机的句柄（命名与动态都有）；过渡期间是目标；没有活动相机时为 <c>default</c>。</summary>
        /// <remarks>命名相机的 string id 在其 <see cref="CameraHandle.NamedId"/> 里（动态为 <c>null</c>）；门面便利
        /// <c>CameraManager.ActiveCameraId</c> 直接读它，O(1)。</remarks>
        CameraHandle ActiveHandle { get; }

        /// <summary>是否有相机过渡在途。</summary>
        bool IsTransitioning { get; }

        /// <summary>立即切换到指定命名相机（cut）。</summary>
        /// <param name="cameraId">已注册的命名相机 id。</param>
        /// <remarks>会取消在途过渡。未知 id 告警并 no-op；<c>null</c> 抛异常。</remarks>
        void Activate(string cameraId);

        /// <summary>反激活指定命名相机。</summary>
        /// <param name="cameraId">已注册的命名相机 id。</param>
        /// <remarks>是否回退到另一台相机由后端定义（默认实现不回退；Cinemachine 由 Brain 的优先级队列决定）。
        /// 未知 id 告警并 no-op；<c>null</c> 抛异常。</remarks>
        void Deactivate(string cameraId);

        /// <summary>过渡到指定命名相机。</summary>
        /// <param name="cameraId">已注册的命名相机 id。</param>
        /// <param name="duration">过渡时长（秒）；<c>&lt;= 0.0001</c>（内置实现的 Epsilon）等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式；后端可将其作为建议而非强制契约。</param>
        /// <remarks>过渡期间再次调用会从当前视觉状态重新开始；未知 id 告警并 no-op；<c>null</c> 抛异常。</remarks>
        void TransitionTo(string cameraId, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut);

        /// <summary>指定命名相机是否为当前活动（过渡期间的目标也算活动）。</summary>
        /// <param name="cameraId">相机 id。</param>
        bool IsActive(string cameraId);

        /// <summary>注册一台动态相机，返回其后端句柄。</summary>
        /// <typeparam name="T">后端相机类型；由实现解释（UnityCameraManager 要 <see cref="Camera"/>，
        /// Cinemachine 实现要它的 vcam 基类）。</typeparam>
        /// <param name="handle">后端相机对象。</param>
        /// <returns>动态相机句柄；可用 <see cref="CameraHandle.Activate"/> / <see cref="Unregister(CameraHandle)"/> 操作。</returns>
        /// <remarks>类型不符抛 <see cref="System.ArgumentException"/>；<c>null</c> 抛
        /// <see cref="System.ArgumentNullException"/>。注册不会激活相机。</remarks>
        CameraHandle Register<T>(T handle) where T : class;

        /// <summary>注销一台动态相机。</summary>
        /// <param name="handle">寄存器返回的句柄。</param>
        /// <returns>确实移除了才返回 <c>true</c>；<c>default</c> / 已注销 / 无效句柄返回 <c>false</c>。</returns>
        /// <remarks>若它在活动 / 过渡目标上，实现会清掉活动状态（不回退到另一台相机）。
        /// <para><b>只清管理器视图</b>：Cinemachine 适配器不会让 Brain 停止使用该 vcam——注销后 Brain 仍可能
        /// 继续输出它，需要另行停用 / 降优先级（CAM-005）。</para></remarks>
        bool Unregister(CameraHandle handle);

        /// <summary>立即切换到指定句柄的相机（cut）。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <remarks>无效 / <c>default</c> 句柄告警并 no-op。</remarks>
        void Activate(CameraHandle handle);

        /// <summary>反激活指定句柄的相机。</summary>
        /// <param name="handle">相机句柄。</param>
        void Deactivate(CameraHandle handle);

        /// <summary>过渡到指定句柄的相机。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <param name="duration">过渡时长（秒）；<c>&lt;= 0.0001</c>（内置实现的 Epsilon）等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        void TransitionTo(CameraHandle handle, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut);

        /// <summary>指定句柄的相机是否为当前活动（过渡期间的目标也算活动）。</summary>
        /// <param name="handle">相机句柄。</param>
        bool IsActive(CameraHandle handle);
    }
}
