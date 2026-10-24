using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机后端契约：只提供「注册 / 激活 / 清空 / 查询」等原语，栈、异步与消息由门面侧的统一导演层负责。
    /// <para><b>这不是游戏代码的日常入口</b>：游戏代码使用 <see cref="CameraManager"/> 与
    /// <see cref="CameraManager.TryGetCamera"/>。本接口是 internal 实现 / 测试缝。</para>
    /// <para><b>身份</b>：相机以字符串 id 标识；显式 id 由调用方给出，动态相机的 id 由导演层生成并以
    /// <c>@auto/</c> 开头。实现不得解析或改写 id。</para>
    /// <para><b>线程</b>：全部成员仅在主线程调用。</para>
    /// <para><b>失败语义</b>：未知 id / 已销毁相机由实现告警并 no-op；<c>null</c> id 抛
    /// <see cref="ArgumentNullException"/>。注册冲突、类型不符、已销毁对象抛 <see cref="ArgumentException"/>。</para>
    /// </summary>
    internal interface ICameraBackend : IDisposable
    {
        /// <summary>Brain / 输出驱动当前活动相机解析后的 id；无活动或活动相机未注册时为 <c>null</c>。
        /// 分组相机（如 ClearShot）的活动子相机应向上解析到已注册的父相机 id。</summary>
        string ActiveCameraId { get; }

        /// <summary>Brain / 输出驱动当前活动相机；无活动 / 非 vcam 时为 <c>null</c>。</summary>
        CinemachineVirtualCameraBase ActiveCamera { get; }

        /// <summary>输出驱动当前是否有活动相机（即使它未注册）。</summary>
        bool HasActiveCamera { get; }

        /// <summary>当前输出相机；可能为 <c>null</c>。</summary>
        Camera OutputCamera { get; }

        /// <summary>输出驱动是否正在混合（含外部系统发起的混合）。</summary>
        bool IsBlending { get; }

        /// <summary>本管理器发起且尚未完成的<b>非 cut</b>过渡是否为在途。</summary>
        bool IsTransitioning { get; }

        /// <summary>管理器过渡进度（0..1）；无管理器过渡时为 0。</summary>
        float TransitionProgress { get; }

        /// <summary>在途请求的目标 id；无请求时为 <c>null</c>（cut 在结算前也算请求）。</summary>
        string TransitionTargetId { get; }

        /// <summary>已注册相机数量（含已销毁但尚未注销的槽位）。</summary>
        int CameraCount { get; }

        /// <summary>显式 id 相机数量（含已销毁但尚未注销的槽位）。</summary>
        int NamedCount { get; }

        /// <summary>生成 id 相机数量（含已销毁但尚未注销的槽位）。</summary>
        int GeneratedCount { get; }

        /// <summary>注册一台相机；不激活、不改变场景状态。</summary>
        /// <param name="cameraId">相机 id；非 <c>null</c>。</param>
        /// <param name="camera">后端相机对象。</param>
        /// <param name="isGenerated">该 id 是否为导演层生成的动态 id。</param>
        /// <returns>确实新增返回 <c>true</c>；同一 id + 同一相机重复注册返回 <c>false</c>（幂等）。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 或 <paramref name="camera"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">id 已被另一相机占用、相机已注册为另一 id、类型不符或对象已销毁。</exception>
        bool Register(string cameraId, object camera, bool isGenerated);

        /// <summary>注销一台相机；不销毁相机对象。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <returns>确实移除了才返回 <c>true</c>；未知 id 返回 <c>false</c>。</returns>
        /// <remarks>恢复它被管理器改写的作者状态。只有被注销的正是当前在途请求的目标时才结算该请求并强制
        /// 完成混合；注销其它相机不影响在途过渡。</remarks>
        bool Unregister(string cameraId);

        /// <summary>id 是否在注册表中（相机已销毁但尚未注销时仍为 <c>true</c>）。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        bool IsRegistered(string cameraId);

        /// <summary>按 id 取回后端相机对象；未知 id 或对象已销毁返回 <c>false</c>。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="camera">找到且存活时为后端相机对象；否则为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        bool TryGetCamera(string cameraId, out object camera);

        /// <summary>把已注册 id 按插入顺序复制到 buffer（含已销毁但尚未注销的槽位）；返回写入数量。</summary>
        /// <param name="buffer">目标 buffer；方法先清空它。</param>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> 为 <c>null</c>。</exception>
        int CopyCameraIds(List<string> buffer);

        /// <summary>激活指定相机；<paramref name="duration"/> 为 0 或样式为 Cut 时立即切换。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="duration">过渡时长（秒）；NaN / ±∞ 抛 <see cref="ArgumentOutOfRangeException"/>。</param>
        /// <param name="style">过渡样式。</param>
        /// <remarks>未知 id / 已销毁相机告警并 no-op；不抛。实现应把目标提升到同通道场景最大优先级之上，
        /// 并恢复上一个由它改写的相机的作者优先级。</remarks>
        void Activate(string cameraId, float duration, CameraBlendStyle style);

        /// <summary>清空本管理器的激活请求并恢复它改写的优先级；输出驱动自行重新选择。</summary>
        /// <remarks>不销毁相机、不保证输出驱动停止输出某一台相机。</remarks>
        void ClearActive();

        /// <summary>设置相机的跟随目标；<c>null</c> 清除跟随目标。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="follow">跟随目标。</param>
        /// <remarks>未知 id / 已销毁相机告警并 no-op。分组相机写 <c>DefaultTarget.Target.TrackingTarget</c>，
        /// 不改变 <c>DefaultTarget.Enabled</c>。</remarks>
        void SetFollow(string cameraId, Transform follow);

        /// <summary>设置相机的瞄准目标；<c>null</c> 清除本地覆盖并回退到默认瞄准目标。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="lookAt">瞄准目标。</param>
        /// <remarks>未知 id / 已销毁相机告警并 no-op。内置后端对 <c>CinemachineCamera</c> 复位
        /// <c>Target.CustomLookAtTarget</c>（回退到跟随目标），对分组相机复位 <c>DefaultTarget.Target</c>
        /// 的同名字段（不改变 <c>DefaultTarget.Enabled</c>）。</remarks>
        void SetLookAt(string cameraId, Transform lookAt);

        /// <summary>设置相机的视场角（立即生效）。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="fieldOfView">视场角；必须为有限正数。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="fieldOfView"/> 非有限或 <c>&lt;= 0</c>。</exception>
        /// <remarks>不支持视场角的后端 / 相机（如分组相机）告警并 no-op。</remarks>
        void SetFieldOfView(string cameraId, float fieldOfView);

        /// <summary>触发一次震屏。</summary>
        /// <param name="strength">强度；必须为有限正数（1 为常规强度）。</param>
        /// <param name="duration">时长（秒）；必须为有限正数。</param>
        /// <exception cref="ArgumentOutOfRangeException">参数非有限或 <c>&lt;= 0</c>。</exception>
        /// <remarks>无活动相机 / 输出驱动已销毁时告警并 no-op。</remarks>
        void Shake(float strength, float duration);

        /// <summary>输出驱动的 incoming 切换时触发（混合开始帧；cut 同帧）。</summary>
        /// <remarks>未注册 / 外部 incoming 的 id 为 <c>null</c>。</remarks>
        event Action<CameraBackendActivation> Activated;

        /// <summary>相机不再 live 时触发（混合结束时才触发）；未注册 / 外部相机的 id 为 <c>null</c>。</summary>
        event Action<string> Deactivated;

        /// <summary>相机完成 blend-in 或作为 cut 被激活时触发；未注册 / 外部相机的 id 为 <c>null</c>。</summary>
        event Action<string> BlendCompleted;

        /// <summary>一次激活请求结算时触发（每个请求恰好一次）。</summary>
        /// <remarks>第一个参数为请求目标 id；第二个参数为结算结果。同步命令没有等待者时也会触发。</remarks>
        event Action<string, CameraTransitionResult> RequestSettled;
    }

    /// <summary>
    /// 一次激活事件的载荷：incoming / outgoing 的 id 与是否 cut。
    /// <para>id 为 <c>null</c> 表示该端是外部 / 未注册相机。</para>
    /// </summary>
    internal readonly struct CameraBackendActivation
    {
        /// <summary>incoming 相机 id；外部 / 未注册为 <c>null</c>。</summary>
        public readonly string IncomingCameraId;

        /// <summary>outgoing 相机 id；无 outgoing 或未注册为 <c>null</c>。</summary>
        public readonly string OutgoingCameraId;

        /// <summary>本次切换是否为 cut。</summary>
        public readonly bool IsCut;

        /// <summary>构造载荷。</summary>
        /// <param name="incomingCameraId">incoming 相机 id。</param>
        /// <param name="outgoingCameraId">outgoing 相机 id。</param>
        /// <param name="isCut">是否为 cut。</param>
        public CameraBackendActivation(string incomingCameraId, string outgoingCameraId, bool isCut)
        {
            IncomingCameraId = incomingCameraId;
            OutgoingCameraId = outgoingCameraId;
            IsCut = isCut;
        }
    }

    /// <summary>
    /// 会话复位可选能力：只退订静态事件并清引用，不 Dispose、不碰 Unity 场景对象。
    /// <para>由门面在会话复位时探测调用。关闭域重载时静态订阅会跨会话存活，不显式退订会让旧后端被钉住。</para>
    /// </summary>
    internal interface ICameraBackendSessionReset
    {
        /// <summary>退订静态事件并清空内部引用；不得访问 Unity 场景对象。</summary>
        void UnbindSessionEvents();
    }
}
