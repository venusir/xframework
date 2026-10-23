using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机后端契约：只提供「注册 / 激活 / 清空 / 查询」等原语，栈、异步与消息由门面侧的统一导演层负责。
    /// <para><b>这不是游戏代码的日常入口</b>：游戏代码使用 <see cref="CameraManager"/> 与
    /// <see cref="CameraManager.TryGetCamera{T}"/>。只有实现新的相机后端时才需要本接口。</para>
    /// <para><b>身份</b>：相机以字符串 id 标识；显式 id 由调用方给出，动态相机的 id 由导演层生成并以
    /// <c>@auto/</c> 开头。实现不得解析或改写 id。</para>
    /// <para><b>线程</b>：全部成员仅在主线程调用。</para>
    /// <para><b>失败语义</b>：未知 id / 已销毁相机由实现告警并 no-op；<c>null</c> id 抛
    /// <see cref="ArgumentNullException"/>。注册冲突、类型不符、已销毁对象抛 <see cref="ArgumentException"/>。</para>
    /// </summary>
    public interface ICameraBackend : IDisposable
    {
        /// <summary>Brain / 输出驱动当前活动相机解析后的 id；无活动或活动相机未注册时为 <c>null</c>。
        /// 分组相机（如 ClearShot）的活动子相机应向上解析到已注册的父相机 id。</summary>
        string ActiveCameraId { get; }

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
    }

    /// <summary>
    /// 会话复位可选能力：只退订静态事件并清引用，不 Dispose、不碰 Unity 场景对象。
    /// <para>由门面在会话复位时探测调用。关闭域重载时静态订阅会跨会话存活，不显式退订会让旧后端被钉住。</para>
    /// </summary>
    public interface ICameraBackendSessionReset
    {
        /// <summary>退订静态事件并清空内部引用；不得访问 Unity 场景对象。</summary>
        void UnbindSessionEvents();
    }
}
