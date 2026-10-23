using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera.Cinemachine;
using XFramework.XCamera.Messages;
using XFramework.XLog;
using XFramework.XMessage;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机管理的静态门面：以 string id 与游戏语言（切换 / 压栈 / 弹栈 / 查询）操作相机。
    /// <para><b>为什么需要它</b>：游戏代码需要「切到哪台相机 / 临时覆盖 / 返回 / 当前输出相机是谁」，
    /// 但不该被后端的注册表、优先级与混合细节淹没。门面把这件事收敛成基础相机 + 覆盖栈；
    /// 跟随 / 瞄准 / 震屏等行为仍由具体后端（内置后端为 Cinemachine）执行。</para>
    /// <para><b>边界</b>：gameplay 动词（切 / 压 / 弹 / 跟随 / 震屏 / 查询 / 订阅）只使用 string id 与中立类型；
    /// Cinemachine 类型只出现在初始化、注册与高级访问（<see cref="TryGetCamera"/> / <see cref="ActiveCamera"/>），
    /// 避免 CM 引用扩散到 gameplay 代码。</para>
    /// <para><b>相机栈</b>：<see cref="SwitchTo"/> 设定基础相机并清空覆盖；<see cref="Push"/> 压入临时覆盖；
    /// <see cref="Pop"/> 返回上一层；<see cref="PopAll"/> 回到基础；<see cref="Clear"/> 释放全部请求。</para>
    /// <para><b>唯一身份</b>：显式 id 由调用方给出；动态相机经 <see cref="Register(CinemachineVirtualCameraBase)"/>
    /// 获得运行时生成的 <c>@auto/N</c> id。</para>
    /// <para><b>探测型读宽容</b>：<see cref="ActiveCameraId"/> / <see cref="BaseCameraId"/> /
    /// <see cref="StackDepth"/> / <see cref="HasActiveCamera"/> / <see cref="OutputCamera"/> /
    /// <see cref="IsBlending"/> / <see cref="IsExternallyControlled"/> / <see cref="CameraCount"/> /
    /// <see cref="GetState"/> / <see cref="CopyCameras"/> / <see cref="TryGetCamera"/> 在未初始化时返回默认值；
    /// 注册与变更命令仍抛 <see cref="InvalidOperationException"/>。</para>
    /// <para><b>所有权</b>：<see cref="Initialize"/> 后门面拥有内部后端，<see cref="Destroy"/> 会释放它，
    /// 但不销毁 Brain。<see cref="ResetForNewSession"/> 只丢引用、不 Dispose。</para>
    /// <example>
    /// <code>
    /// CameraManager.Initialize(brain);
    /// CameraManager.Register("gameplay", gameplayCam);
    /// CameraManager.Register("aim", aimCam);
    /// CameraManager.SwitchTo("gameplay");
    /// CameraManager.Push("aim", 0.2f);   // 开镜
    /// CameraManager.Pop(0.25f);          // 收镜
    /// var camera = CameraManager.OutputCamera; // 世界↔屏幕、射线直接用 UnityEngine.Camera
    /// </code>
    /// </example>
    /// </summary>
    public static class CameraManager
    {
        #region Private Fields

        private static CameraDirector _director;

        #endregion

        #region Public API — Probes

        /// <summary>当前是否已注入后端。</summary>
        public static bool IsInitialized => _director != null;

        /// <summary>当前活动相机 id；无活动或活动相机未注册时为 <c>null</c>。</summary>
        /// <remarks>读输出驱动的真实状态，请求后可能晚一次更新才可见；分组相机的活动子相机会解析到父相机 id。</remarks>
        public static string ActiveCameraId => _director != null ? _director.ActiveCameraId : null;

        /// <summary>当前活动 vcam；无活动或活动相机不是 vcam 时为 <c>null</c>。</summary>
        /// <remarks>高级访问用。gameplay 代码通常只需要 <see cref="ActiveCameraId"/> 与
        /// <see cref="OutputCamera"/>；需要 Lens / Aim / Confiner 时再用本属性或 <see cref="TryGetCamera"/>。</remarks>
        public static CinemachineVirtualCameraBase ActiveCamera => _director?.ActiveCamera;

        /// <summary>相机栈的基础相机 id；栈空时为 <c>null</c>。</summary>
        public static string BaseCameraId => _director != null ? _director.BaseCameraId : null;

        /// <summary>相机栈深度（含基础相机）；未初始化时为 0。</summary>
        public static int StackDepth => _director != null ? _director.StackDepth : 0;

        /// <summary>输出驱动当前是否有活动相机（即使它未注册）；未初始化时为 <c>false</c>。</summary>
        public static bool HasActiveCamera => _director != null && _director.HasActiveCamera;

        /// <summary>当前输出相机；未初始化或没有输出相机时为 <c>null</c>。</summary>
        /// <remarks>不要用 <c>OutputCamera != null</c> 判断「是否有已注册的活动相机」；请用
        /// <see cref="ActiveCameraId"/> / <see cref="HasActiveCamera"/>。</remarks>
        public static Camera OutputCamera => _director?.OutputCamera;

        /// <summary>输出驱动是否正在混合（含外部系统发起的混合）；未初始化时为 <c>false</c>。</summary>
        public static bool IsBlending => _director != null && _director.IsBlending;

        /// <summary>输出驱动的活动相机是否不是本管理器请求的栈顶（外部接管）；未初始化时为 <c>false</c>。</summary>
        public static bool IsExternallyControlled =>
            _director != null && _director.IsExternallyControlled;

        /// <summary>已注册相机数量（含已销毁但尚未注销的槽位）；未初始化时为 0。</summary>
        public static int CameraCount => _director != null ? _director.CameraCount : 0;

        /// <summary>读取只读诊断快照；未初始化时返回 <c>default</c>。</summary>
        public static CameraStateSnapshot GetState()
        {
            return _director != null ? _director.GetState() : default;
        }

        /// <summary>把逐相机诊断信息复制到调用方 buffer；未初始化时清空并返回 0。</summary>
        /// <param name="buffer">目标 buffer；方法先清空它。</param>
        /// <returns>写入数量。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> 为 <c>null</c>。</exception>
        public static int CopyCameras(List<CameraInfo> buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (_director == null)
            {
                buffer.Clear();
                return 0;
            }

            return _director.CopyCameras(buffer);
        }

        /// <summary>按 id 取回 vcam（高级访问）；未初始化 / 未知 id / 已销毁返回 <c>false</c>。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="camera">找到时为 vcam；否则为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <remarks>用于需要直接配置 Lens / Aim / Confiner 等 Cinemachine 能力的高级代码；日常游戏逻辑请用门面动词。</remarks>
        public static bool TryGetCamera(string cameraId, out CinemachineVirtualCameraBase camera)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (_director == null)
            {
                camera = null;
                return false;
            }

            return _director.TryGetCamera(cameraId, out camera);
        }

        #endregion

        #region Public API — Registry

        /// <summary>注册一台命名相机，返回 id；注册不激活、不改变场景状态。</summary>
        /// <param name="cameraId">显式 id；不得为空、全空白或以 <c>@auto/</c> 开头。</param>
        /// <param name="camera">vcam。</param>
        /// <returns>相机 id。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 或 <paramref name="camera"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">相机对象已销毁、id 已占用，或相机已注册为另一 id。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static string Register(string cameraId, CinemachineVirtualCameraBase camera)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            ValidateCameraArgument(camera, nameof(camera));
            EnsureInitialized();
            return _director.Register(cameraId, camera);
        }

        /// <summary>注册一台动态相机，返回运行时生成的 id；注册不激活、不改变场景状态。</summary>
        /// <param name="camera">vcam。</param>
        /// <returns>生成的 <c>@auto/N</c> id。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="camera"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">相机对象已销毁，或相机已注册为另一 id。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static string Register(CinemachineVirtualCameraBase camera)
        {
            ValidateCameraArgument(camera, nameof(camera));
            EnsureInitialized();
            return _director.Register(camera);
        }

        /// <summary>注销一台相机；不销毁相机对象。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <returns>确实移除了才返回 <c>true</c>；未初始化 / 未知 id 返回 <c>false</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        public static bool Unregister(string cameraId)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            return _director != null && _director.Unregister(cameraId);
        }

        #endregion

        #region Public API — Playback

        /// <summary>切换到指定相机（设定基础相机并清空覆盖）。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="blendDuration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="blendDuration"/> 非有限值。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        /// <remarks>未知 id / 已销毁相机告警并 no-op。</remarks>
        public static void SwitchTo(string cameraId, float blendDuration = 0f,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            EnsureInitialized();
            _director.SwitchTo(cameraId, blendDuration, style);
        }

        /// <summary>压入一层临时覆盖相机。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="blendDuration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="blendDuration"/> 非有限值。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        /// <remarks>目标已是栈顶时 no-op（外部接管时会重新激活）；未知 id / 已销毁相机告警并 no-op。</remarks>
        public static void Push(string cameraId, float blendDuration = 0f,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            EnsureInitialized();
            _director.Push(cameraId, blendDuration, style);
        }

        /// <summary>弹出栈顶覆盖层，回到上一层。</summary>
        /// <param name="blendDuration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="blendDuration"/> 非有限值。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        /// <remarks>只有基础相机时告警并 no-op。</remarks>
        public static void Pop(float blendDuration = 0f,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            EnsureInitialized();
            _director.Pop(blendDuration, style);
        }

        /// <summary>清空全部覆盖层，回到基础相机。</summary>
        /// <param name="blendDuration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="blendDuration"/> 非有限值。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static void PopAll(float blendDuration = 0f,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            EnsureInitialized();
            _director.PopAll(blendDuration, style);
        }

        /// <summary>释放本管理器的全部相机请求；输出驱动自行重新选择。</summary>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static void Clear()
        {
            EnsureInitialized();
            _director.Clear();
        }

        /// <summary>切换到指定相机并等待完成。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="blendDuration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        /// <param name="cancellationToken">只取消等待，不中止相机过渡。</param>
        /// <returns>完成结果。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="blendDuration"/> 非有限值。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static UniTask<CameraTransitionResult> SwitchToAsync(string cameraId,
            float blendDuration = 0f, CameraBlendStyle style = CameraBlendStyle.EaseInOut,
            CancellationToken cancellationToken = default)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            EnsureInitialized();
            return _director.SwitchToAsync(cameraId, blendDuration, style, cancellationToken);
        }

        /// <summary>压入覆盖层并等待完成。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="blendDuration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        /// <param name="cancellationToken">只取消等待，不中止相机过渡。</param>
        /// <returns>完成结果。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="blendDuration"/> 非有限值。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static UniTask<CameraTransitionResult> PushAsync(string cameraId,
            float blendDuration = 0f, CameraBlendStyle style = CameraBlendStyle.EaseInOut,
            CancellationToken cancellationToken = default)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            EnsureInitialized();
            return _director.PushAsync(cameraId, blendDuration, style, cancellationToken);
        }

        /// <summary>弹出栈顶覆盖层并等待回到上一层。</summary>
        /// <param name="blendDuration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        /// <param name="cancellationToken">只取消等待，不中止相机过渡。</param>
        /// <returns>完成结果；只有基础相机时立即 <see cref="CameraTransitionResult.Completed"/>。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="blendDuration"/> 非有限值。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static UniTask<CameraTransitionResult> PopAsync(float blendDuration = 0f,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _director.PopAsync(blendDuration, style, cancellationToken);
        }

        /// <summary>清空覆盖层并等待回到基础相机。</summary>
        /// <param name="blendDuration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        /// <param name="cancellationToken">只取消等待，不中止相机过渡。</param>
        /// <returns>完成结果；没有覆盖层时立即 <see cref="CameraTransitionResult.Completed"/>。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="blendDuration"/> 非有限值。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static UniTask<CameraTransitionResult> PopAllAsync(float blendDuration = 0f,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _director.PopAllAsync(blendDuration, style, cancellationToken);
        }

        #endregion

        #region Public API — Bindings

        /// <summary>设置相机的跟随目标；<paramref name="follow"/> 为 <c>null</c> 时清除本地覆盖。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="follow">跟随目标；可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        /// <remarks>未知 id / 已销毁相机告警并 no-op。跟随行为本身仍由后端（内置后端为 Cinemachine）执行。</remarks>
        public static void SetFollow(string cameraId, Transform follow)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            EnsureInitialized();
            _director.SetFollow(cameraId, follow);
        }

        /// <summary>设置相机的瞄准目标；<paramref name="lookAt"/> 为 <c>null</c> 时清除本地覆盖。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="lookAt">瞄准目标；可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        /// <remarks>未知 id / 已销毁相机告警并 no-op。</remarks>
        public static void SetLookAt(string cameraId, Transform lookAt)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            EnsureInitialized();
            _director.SetLookAt(cameraId, lookAt);
        }

        /// <summary>设置相机的视场角（立即生效）。</summary>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="fieldOfView">视场角；必须为有限正数。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="fieldOfView"/> 非有限或 <c>&lt;= 0</c>。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        /// <remarks>分组相机（如 ClearShot）没有 Lens，告警并 no-op；高级需求走
        /// <see cref="TryGetCamera"/> 取 Lens。</remarks>
        public static void SetFieldOfView(string cameraId, float fieldOfView)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            EnsureInitialized();
            _director.SetFieldOfView(cameraId, fieldOfView);
        }

        /// <summary>触发一次震屏。</summary>
        /// <param name="strength">强度；必须为有限正数（1 为常规强度）。</param>
        /// <param name="duration">时长（秒）；必须为有限正数。</param>
        /// <exception cref="ArgumentOutOfRangeException">参数非有限或 <c>&lt;= 0</c>。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        /// <remarks>活动相机需要带 <c>CinemachineImpulseListener</c>；缺失时只告警一次、不自动添加。
        /// 无活动相机 / 输出驱动已销毁时告警并 no-op。</remarks>
        public static void Shake(float strength, float duration = 0.2f)
        {
            EnsureInitialized();
            _director.Shake(strength, duration);
        }

        #endregion

        #region Public API — Events

        /// <summary>订阅相机激活消息；返回退订句柄。</summary>
        /// <param name="handler">消息处理器。</param>
        /// <returns>释放即退订；也可配合 <c>destroyCancellationToken</c> 使用。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 <c>null</c>。</exception>
        /// <remarks>事件经 <see cref="MessageManager"/> 发布，**不依赖相机门面是否初始化**；订阅生命周期由调用方管理。
        /// 外部 / 未注册相机的 <c>CameraId</c> 为 <c>null</c>。</remarks>
        public static IDisposable Subscribe(Action<CameraActivatedMessage> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return MessageManager.Subscribe(handler);
        }

        /// <summary>订阅相机反激活消息；返回退订句柄。</summary>
        /// <param name="handler">消息处理器。</param>
        /// <returns>释放即退订。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 <c>null</c>。</exception>
        public static IDisposable Subscribe(Action<CameraDeactivatedMessage> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return MessageManager.Subscribe(handler);
        }

        /// <summary>订阅相机过渡完成消息；返回退订句柄。</summary>
        /// <param name="handler">消息处理器。</param>
        /// <returns>释放即退订。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 <c>null</c>。</exception>
        public static IDisposable Subscribe(Action<CameraBlendCompletedMessage> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return MessageManager.Subscribe(handler);
        }

        #endregion

        #region Lifecycle

        /// <summary>用输出驱动初始化门面；门面创建并取得内置后端的所有权。</summary>
        /// <param name="brain">场景里控制 Unity Camera 的 Brain。</param>
        /// <exception cref="ArgumentNullException"><paramref name="brain"/> 为 <c>null</c>。</exception>
        /// <remarks>重复调用告警并保留第一个后端；Brain 不被销毁，内置后端在 <see cref="Destroy"/> 时释放。</remarks>
        public static void Initialize(CinemachineBrain brain)
        {
            if (brain == null)
                throw new ArgumentNullException(nameof(brain));
            if (RejectDuplicateInitialize())
                return;

            _director = new CameraDirector(new CinemachineCameraBackend(brain));
        }

        /// <summary>测试注入内部后端；不参与公开 API。</summary>
        internal static void InitializeForTests(ICameraBackend backend)
        {
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));
            if (RejectDuplicateInitialize())
                return;

            _director = new CameraDirector(backend);
        }

        /// <summary>销毁门面并释放注入的后端。</summary>
        /// <remarks>先清槽再 Dispose：后端 Dispose 抛出时不得留下「已销毁却仍被门面持有」的永久闩锁
        /// （CAM-002）；Dispose 异常继续向调用方传播。</remarks>
        public static void Destroy()
        {
            var director = _director;
            _director = null;
            director?.Dispose();
        }

        #endregion

        #region Session Reset

        /// <summary>
        /// 会话开始时复位静态状态：只丢引用、不 Dispose。
        /// <para>关闭域重载时上一会话的静态字段会存活；门面在正常游戏流程里不会被
        /// <see cref="Destroy"/>，不复位会让第二会话的 <see cref="Initialize"/> 因重复而告警早退。</para>
        /// <para>若旧后端支持 <see cref="ICameraBackendSessionReset"/>，先让它退订静态事件并清引用；
        /// 该路径**不访问 Unity 场景对象**（上一会话的对象可能已销毁）。退订异常只记 Warning，不传播。</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForNewSession()
        {
            var director = _director;
            _director = null;
            director?.ResetForNewSession();
        }

        #endregion

        #region Internal

        private static bool RejectDuplicateInitialize()
        {
            if (_director == null)
                return false;

            LogManager.Warning(LogCategories.CameraManager,
                "CameraManager.Initialize 被重复调用，忽略重复注入；传入的 Brain / 后端未被接管。");
            return true;
        }

        private static void EnsureInitialized()
        {
            if (_director == null)
            {
                throw new InvalidOperationException(
                    "[CameraManager] CameraManager 尚未初始化。请先调用 CameraManager.Initialize(ICameraBackend) 完成初始化。");
            }
        }

        private static void ValidateCameraArgument(CinemachineVirtualCameraBase camera, string paramName)
        {
            if (ReferenceEquals(camera, null))
                throw new ArgumentNullException(paramName);

            if (camera == null)
            {
                throw new ArgumentException(
                    "[CameraManager] 相机对象已被销毁，不能注册。", paramName);
            }
        }

        #endregion
    }
}
