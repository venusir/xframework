using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机管理后端契约：激活 / 反激活 / 过渡与查询。
    /// <para><b>只统一运行期操作</b>：相机的注册（<see cref="Camera"/>、Cinemachine vcam 或自定义句柄）
    /// 是后端相关的，由具体实现类提供；门面与接口不预设句柄类型。</para>
    /// <para><b>线程</b>：全部成员仅在主线程调用。</para>
    /// <para><b>未知 id</b>：实现应告警并 no-op，<b>不得抛异常</b>；<c>null</c> id 抛
    /// <see cref="System.ArgumentNullException"/>。</para>
    /// <para><b>过渡语义</b>：<see cref="ActiveCameraId"/> 在过渡期间返回<b>目标</b>；
    /// <see cref="IsTransitioning"/> 表示是否有过渡在途。过渡中断、回退与曲线细节由后端定义，
    /// 使用方以这两个查询为可移植信号。</para>
    /// </summary>
    public interface ICameraManager
    {
        /// <summary>当前输出相机；没有活动相机时为 <c>null</c>。</summary>
        /// <remarks>过渡期间返回目标的输出相机。使用方可直接用它做世界↔屏幕、射线、FOV 等
        /// <see cref="Camera"/> 操作——本模块刻意不重复封装这些成员。</remarks>
        Camera OutputCamera { get; }

        /// <summary>已注册的相机 id（主线程实时视图）。</summary>
        /// <remarks>枚举期间不要注册 / 注销；实现对返回集合的跨线程一致性不作承诺。</remarks>
        IReadOnlyCollection<string> CameraIds { get; }

        /// <summary>当前活动相机 id；过渡期间是目标 id；没有活动相机时为 <c>null</c>。</summary>
        string ActiveCameraId { get; }

        /// <summary>是否有相机过渡在途。</summary>
        bool IsTransitioning { get; }

        /// <summary>立即切换到指定相机（cut）。</summary>
        /// <param name="cameraId">已注册的相机 id。</param>
        /// <remarks>会取消在途过渡。未知 id 告警并 no-op；<c>null</c> 抛异常。</remarks>
        void Activate(string cameraId);

        /// <summary>反激活指定相机。</summary>
        /// <param name="cameraId">已注册的相机 id。</param>
        /// <remarks>是否回退到另一台相机由后端定义（默认实现不回退；Cinemachine 由 Brain 的优先级队列决定）。
        /// 未知 id 告警并 no-op；<c>null</c> 抛异常。</remarks>
        void Deactivate(string cameraId);

        /// <summary>过渡到指定相机。</summary>
        /// <param name="cameraId">已注册的相机 id。</param>
        /// <param name="duration">过渡时长（秒）；<c>&lt;= 0</c> 等价于 cut。</param>
        /// <param name="style">过渡样式；后端可将其作为建议而非强制契约。</param>
        /// <remarks>过渡期间再次调用会从当前视觉状态重新开始；未知 id 告警并 no-op；<c>null</c> 抛异常。</remarks>
        void TransitionTo(string cameraId, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut);

        /// <summary>指定相机是否为当前活动（过渡期间的目标也算活动）。</summary>
        /// <param name="cameraId">相机 id。</param>
        bool IsActive(string cameraId);
    }
}
