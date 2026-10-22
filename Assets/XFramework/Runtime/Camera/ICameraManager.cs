using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机管理后端契约：命名 / 动态相机统一走 <see cref="CameraHandle"/> 的注册、激活、反激活、过渡与查询。
    /// <para><b>统一句柄</b>：常驻命名相机与随预制创建 / 销毁的动态相机都经
    /// <see cref="Register{T}(string, T)"/> / <see cref="Register{T}(T)"/> 注册并返回句柄；此后所有操作只收句柄。
    /// 命名 id 由句柄的 <see cref="CameraHandle.NamedId"/> 携带（动态为 <c>null</c>），门面便利
    /// <c>CameraManager.ActiveCameraId</c> 直接读它。字符串操作只保留在门面上，由门面解析为句柄。</para>
    /// <para><b>过场边界</b>：随 CG 预制创建、由 Timeline / Cinemachine 接管 Brain 的过场相机
    /// <b>不经过本接口</b>——Timeline 期间调用激活 / 过渡会与过场冲突；这类相机由过场系统管理。</para>
    /// <para><b>线程</b>：全部成员仅在主线程调用。</para>
    /// <para><b>无效句柄 / 未知 id</b>：实现应告警并 no-op，<b>不得抛异常</b>；<c>null</c> id 抛
    /// <see cref="System.ArgumentNullException"/>。无效句柄包括 <c>default(CameraHandle)</c>、
    /// 已注销的旧句柄与代际不符的句柄。</para>
    /// <para><b>过渡语义</b>：<see cref="ActiveHandle"/> 读 Brain 的真实活动相机，可能比请求晚一次 Brain 更新；
    /// <see cref="IsTransitioning"/> 只表示<b>本管理器发起且尚未完成</b>的非 cut 过渡，外部 / Timeline 混合不计入。</para>
    /// </summary>
    public interface ICameraManager
    {
        /// <summary>当前输出相机；没有输出相机时为 <c>null</c>。</summary>
        /// <remarks>使用方可直接用它做世界↔屏幕、射线、FOV 等 <see cref="Camera"/> 操作——本模块刻意不重复封装这些成员。
        /// <para><b>不要用 <c>OutputCamera != null</c> 判断「是否有已注册的活动相机」</b>：Cinemachine 后端返回 Brain 的输出相机，
        /// 即使活动 vcam 未注册也可能非 null，且 Brain 所在对象没有 <see cref="Camera"/> 时为 null；要判断活动状态请用
        /// <see cref="ActiveHandle"/> / <see cref="IsActive(CameraHandle)"/>。</para></remarks>
        Camera OutputCamera { get; }

        /// <summary>Brain 当前活动相机解析后的句柄；过渡期间是 incoming；无活动 / 未注册活动相机时为 <c>default</c>。</summary>
        /// <remarks>命名 id 在 <see cref="CameraHandle.NamedId"/> 里（动态为 <c>null</c>）。该值读 Brain 真实状态，
        /// 请求后要等 Brain 下一次更新才可见。</remarks>
        CameraHandle ActiveHandle { get; }

        /// <summary>本管理器发起且尚未完成的<b>非 cut</b>过渡是否为在途。</summary>
        /// <remarks>外部 / Timeline / 其他系统发起的混合不计入；需要 Brain 真实混合状态请用诊断快照的
        /// <c>IsBrainBlending</c>。</remarks>
        bool IsTransitioning { get; }

        /// <summary>已注册相机数量（含已销毁但尚未注销的槽位）。</summary>
        int CameraCount { get; }

        /// <summary>按命名 id 查找已注册相机句柄。</summary>
        /// <param name="cameraId">命名相机 id。</param>
        /// <param name="handle">找到时为句柄；否则为 <c>default</c>。</param>
        /// <returns>找到返回 <c>true</c>；未知 id 返回 <c>false</c>。</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        bool TryGetHandle(string cameraId, out CameraHandle handle);

        /// <summary>注册一台命名相机，返回句柄；注册不激活、不改变场景状态。</summary>
        /// <typeparam name="T">后端相机类型；由实现解释（Cinemachine 后端要 <c>CinemachineVirtualCameraBase</c>）。</typeparam>
        /// <param name="cameraId">命名 id。</param>
        /// <param name="camera">后端相机对象。</param>
        /// <returns>相机句柄。</returns>
        /// <remarks>同一 id 同一实例重复注册幂等返回同一句柄；id 已被另一实例占用或实例已注册为另一 id 时抛
        /// <see cref="System.ArgumentException"/>。注册已销毁的 Unity 对象抛 <see cref="System.ArgumentException"/>；
        /// 字面 <c>null</c> 抛 <see cref="System.ArgumentNullException"/>。</remarks>
        CameraHandle Register<T>(string cameraId, T camera) where T : class;

        /// <summary>注册一台动态相机，返回句柄；注册不激活、不改变场景状态。</summary>
        /// <typeparam name="T">后端相机类型；由实现解释。</typeparam>
        /// <param name="camera">后端相机对象。</param>
        /// <returns>相机句柄；<see cref="CameraHandle.NamedId"/> 为 <c>null</c>。</returns>
        /// <remarks>同一实例重复注册幂等返回同一句柄；实例已注册为命名相机时抛
        /// <see cref="System.ArgumentException"/>。类型不符抛 <see cref="System.ArgumentException"/>；
        /// 已销毁的 Unity 对象抛 <see cref="System.ArgumentException"/>；字面 <c>null</c> 抛
        /// <see cref="System.ArgumentNullException"/>。</remarks>
        CameraHandle Register<T>(T camera) where T : class;

        /// <summary>注销一台相机。</summary>
        /// <param name="handle">注册时返回的句柄。</param>
        /// <returns>确实移除了才返回 <c>true</c>；<c>default</c> / 已注销 / 无效句柄返回 <c>false</c>。</returns>
        /// <remarks>若它是活动 / 过渡目标，实现会清除本管理器对它施加的优先级 / 启用状态，并按后端规则让 Brain
        /// 重新选择；<b>不销毁相机对象</b>。若作者优先级仍然最高，Brain 可能继续输出它，此时
        /// <see cref="ActiveHandle"/> 为 <c>default</c>。</remarks>
        bool Unregister(CameraHandle handle);

        /// <summary>立即切换到指定相机（cut）。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <remarks>无效 / <c>default</c> / 已销毁相机告警并 no-op。</remarks>
        void Activate(CameraHandle handle);

        /// <summary>反激活指定相机。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <remarks>具体回退由后端定义：Cinemachine 后端抑制该相机的优先级，由 Brain 按优先级队列重选；
        /// 可能选中未注册相机。无效 / <c>default</c> / 已销毁相机告警并 no-op。</remarks>
        void Deactivate(CameraHandle handle);

        /// <summary>过渡到指定相机。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <param name="duration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛
        /// <see cref="System.ArgumentOutOfRangeException"/>。</param>
        /// <param name="style">过渡样式；由后端映射为具体混合曲线。</param>
        /// <remarks>无效 / <c>default</c> / 已销毁相机告警并 no-op。过渡完成由后端事件驱动；
        /// 需要等待请用 <c>CameraManager.TransitionToAsync</c>。</remarks>
        void TransitionTo(CameraHandle handle, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut);

        /// <summary>过渡到指定相机并等待完成。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <param name="duration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛
        /// <see cref="System.ArgumentOutOfRangeException"/>。</param>
        /// <param name="style">过渡样式。</param>
        /// <param name="cancellationToken">只取消等待，不中止相机过渡；取消抛
        /// <see cref="System.OperationCanceledException"/>。</param>
        /// <returns>完成结果；目标已活动且无混合时立即返回 <see cref="CameraTransitionResult.Completed"/>。</returns>
        /// <remarks>无效 / <c>default</c> 句柄返回 <see cref="CameraTransitionResult.TargetUnregistered"/>（并告警）；
        /// 被新命令 / 外部激活取代返回 <see cref="CameraTransitionResult.Superseded"/>；管理器销毁或会话复位返回
        /// <see cref="CameraTransitionResult.ManagerDisposed"/>。</remarks>
        UniTask<CameraTransitionResult> TransitionToAsync(CameraHandle handle, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut,
            CancellationToken cancellationToken = default);

        /// <summary>指定句柄的相机是否为 Brain 当前活动（过渡期间 incoming 也算活动）。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <returns>活动返回 <c>true</c>；无效 / <c>default</c> / 已销毁 / 未注册返回 <c>false</c>。</returns>
        bool IsActive(CameraHandle handle);
    }

    /// <summary>
    /// 会话复位能力：只退订静态事件并清引用，不 Dispose、不碰 Unity 场景对象。
    /// <para>由门面在 <c>CameraManager.ResetForNewSession</c> 时探测调用；仅内置 Cinemachine 后端实现。
    /// 关闭域重载时静态事件会跨会话存活，不显式退订会让旧管理器被钉住。</para>
    /// </summary>
    internal interface ICameraManagerSessionReset
    {
        /// <summary>退订静态事件、清空 pending 与槽位引用；不得访问 Unity 场景对象。</summary>
        void UnbindSessionEvents();
    }
}
