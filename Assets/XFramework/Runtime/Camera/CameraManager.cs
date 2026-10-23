using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机管理的静态门面：以 string id 与游戏语言（切换 / 压栈 / 弹栈 / 查询）操作相机。
    /// <para><b>为什么需要它</b>：游戏代码需要「切到哪台相机 / 临时覆盖 / 返回 / 当前输出相机是谁」，
    /// 但不该被后端的注册表、优先级与混合细节淹没。门面把这件事收敛成基础相机 + 覆盖栈；
    /// 跟随 / 瞄准 / 震屏等行为仍由具体后端（内置后端为 Cinemachine）执行。</para>
    /// <para><b>相机栈</b>：<see cref="SwitchTo"/> 设定基础相机并清空覆盖；<see cref="Push"/> 压入临时覆盖；
    /// <see cref="Pop"/> 返回上一层；<see cref="PopAll"/> 回到基础；<see cref="Clear"/> 释放全部请求。</para>
    /// <para><b>唯一身份</b>：显式 id 由调用方给出；动态相机经 <see cref="Register(UnityEngine.Object)"/>
    /// 获得运行时生成的 <c>@auto/N</c> id。</para>
    /// <para><b>探测型读宽容</b>：<see cref="ActiveCameraId"/> / <see cref="BaseCameraId"/> /
    /// <see cref="StackDepth"/> / <see cref="HasActiveCamera"/> / <see cref="OutputCamera"/> /
    /// <see cref="IsBlending"/> / <see cref="IsExternallyControlled"/> / <see cref="CameraCount"/> /
    /// <see cref="GetState"/> / <see cref="CopyCameras"/> / <see cref="TryGetCamera{T}"/> 在未初始化时返回默认值；
    /// 注册与变更命令仍抛 <see cref="InvalidOperationException"/>。</para>
    /// <para><b>所有权</b>：<see cref="Initialize"/> 后门面拥有后端，<see cref="Destroy"/> 会释放它。
    /// <see cref="ResetForNewSession"/> 只丢引用、不 Dispose。</para>
    /// <example>
    /// <code>
    /// var backend = new CinemachineCameraBackend(brain);
    /// CameraManager.Initialize(backend);
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

        /// <summary>取回已注册的后端相机对象（高级逃生舱）；未初始化 / 未知 id / 已销毁返回 <c>false</c>。</summary>
        /// <typeparam name="T">调用方期望取回的类型，如内置后端的 <c>CinemachineCamera</c>。</typeparam>
        /// <param name="cameraId">相机 id。</param>
        /// <param name="camera">找到且类型匹配时为相机对象；否则为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <remarks>用于需要直接配置 Lens / Aim / Confiner 等后端专有能力的高级代码；日常游戏逻辑请用门面动词。</remarks>
        public static bool TryGetCamera<T>(string cameraId, out T camera) where T : class
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
        /// <param name="camera">后端相机对象。</param>
        /// <returns>相机 id。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 或 <paramref name="camera"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">相机对象已销毁、id 已占用、相机已注册为另一 id，或后端不接受该类型。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static string Register(string cameraId, UnityEngine.Object camera)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            ValidateCameraArgument(camera, nameof(camera));
            EnsureInitialized();
            return _director.Register(cameraId, camera);
        }

        /// <summary>注册一台动态相机，返回运行时生成的 id；注册不激活、不改变场景状态。</summary>
        /// <param name="camera">后端相机对象。</param>
        /// <returns>生成的 <c>@auto/N</c> id。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="camera"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException">相机对象已销毁、相机已注册为另一 id，或后端不接受该类型。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static string Register(UnityEngine.Object camera)
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

        #endregion

        #region Lifecycle

        /// <summary>注入相机后端；门面取得所有权。</summary>
        /// <param name="backend">相机后端。</param>
        /// <exception cref="ArgumentNullException"><paramref name="backend"/> 为 <c>null</c>。</exception>
        /// <remarks>重复调用告警并保留第一个后端；<b>被拒绝的第二个实例仍归调用方所有</b>，请自行释放
        /// （内置后端在构造时订阅静态事件，未释放会泄漏监听器）。</remarks>
        public static void Initialize(ICameraBackend backend)
        {
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));

            if (_director != null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "CameraManager.Initialize 被重复调用，忽略重复注入；传入的实例未被接管，请自行释放。");
                return;
            }

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

        private static void EnsureInitialized()
        {
            if (_director == null)
            {
                throw new InvalidOperationException(
                    "[CameraManager] CameraManager 尚未初始化。请先调用 CameraManager.Initialize(ICameraBackend) 完成初始化。");
            }
        }

        private static void ValidateCameraArgument(UnityEngine.Object camera, string paramName)
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
