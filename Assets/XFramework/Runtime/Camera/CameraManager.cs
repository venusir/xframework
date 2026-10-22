using System;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机管理的静态门面：注入后端实现，转发统一句柄的注册 / 激活 / 反激活 / 过渡与查询。
    /// <para><b>为什么需要它</b>：游戏代码需要「切到哪台相机 / 何时过渡 / 当前输出相机是谁」，但不该直接依赖
    /// Cinemachine 的 vcam 与 Brain 类型。门面把这件事收敛成一个后端中立的入口；相机编排
    /// （跟随 / 瞄准 / 震屏 / 约束）仍归 Cinemachine，本模块只做状态与过渡。</para>
    /// <para><b>统一句柄</b>：命名相机与动态相机都先注册、拿到 <see cref="CameraHandle"/>，之后所有操作只收句柄；
    /// 字符串入口是本门面的便捷重载，内部解析为句柄。</para>
    /// <para><b>探测型读宽容</b>：<see cref="OutputCamera"/> / <see cref="ActiveHandle"/> / <see cref="ActiveCameraId"/> /
    /// <see cref="IsTransitioning"/> / <see cref="CameraCount"/> / <see cref="TryGetHandle"/> /
    /// <see cref="IsActive(CameraHandle)"/> 在未初始化时返回默认值而不抛；注册与变更命令仍抛
    /// <see cref="InvalidOperationException"/>。</para>
    /// <para><b>所有权</b>：<see cref="Initialize"/> 后门面拥有实现，<see cref="Destroy"/> 会释放它
    /// （若实现 <see cref="IDisposable"/>）。<see cref="ResetForNewSession"/> 只丢引用、不 Dispose。</para>
    /// <example>
    /// <code>
    /// var backend = new CinemachineCameraManager(brain);
    /// CameraManager.Initialize(backend);
    /// CameraManager.Register("main", mainVcam);
    /// CameraManager.Register("battle", battleVcam);
    /// CameraManager.Activate("main");
    /// CameraManager.TransitionTo("battle", 0.8f, CameraBlendStyle.EaseInOut);
    /// var camera = CameraManager.OutputCamera; // 世界↔屏幕、射线直接用 UnityEngine.Camera
    ///
    /// // 随预制创建 / 销毁的动态相机：注册拿句柄，销毁时 Unregister / Dispose
    /// var handle = CameraManager.Register(prefabVcam);
    /// CameraManager.Activate(handle);
    /// CameraManager.Unregister(handle); // 或 handle.Dispose()
    /// </code>
    /// </example>
    /// </summary>
    public static class CameraManager
    {
        #region Private Fields

        private static ICameraManager _impl;

        #endregion

        #region Public API — Probes

        /// <summary>当前是否有已注入的实现。</summary>
        public static bool IsInitialized => _impl != null;

        /// <summary>当前输出相机；未初始化或没有输出相机时为 <c>null</c>。</summary>
        public static Camera OutputCamera => _impl != null ? _impl.OutputCamera : null;

        /// <summary>Brain 当前活动相机解析后的句柄；未初始化 / 无活动 / 未注册活动相机时为 <c>default</c>。</summary>
        public static CameraHandle ActiveHandle => _impl != null ? _impl.ActiveHandle : default;

        /// <summary>当前活动相机的命名 id；动态相机活动或无活动相机时为 <c>null</c>。</summary>
        /// <remarks>= <see cref="ActiveHandle"/>.<see cref="CameraHandle.NamedId"/>（O(1)、零遍历）。</remarks>
        public static string ActiveCameraId => ActiveHandle.NamedId;

        /// <summary>本管理器发起且尚未完成的非 cut 过渡是否为在途；未初始化时为 <c>false</c>。</summary>
        public static bool IsTransitioning => _impl != null && _impl.IsTransitioning;

        /// <summary>已注册相机数量（含已销毁但尚未注销的槽位）；未初始化时为 <c>0</c>。</summary>
        public static int CameraCount => _impl != null ? _impl.CameraCount : 0;

        /// <summary>按命名 id 查找已注册相机句柄；未初始化或未知 id 返回 <c>false</c>。</summary>
        /// <param name="cameraId">命名相机 id。</param>
        /// <param name="handle">找到时为句柄；否则为 <c>default</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        public static bool TryGetHandle(string cameraId, out CameraHandle handle)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (_impl == null)
            {
                handle = default;
                return false;
            }

            return _impl.TryGetHandle(cameraId, out handle);
        }

        /// <summary>指定句柄的相机是否为 Brain 当前活动；未初始化 / 无效句柄返回 <c>false</c>。</summary>
        /// <param name="handle">相机句柄。</param>
        public static bool IsActive(CameraHandle handle)
        {
            return _impl != null && _impl.IsActive(handle);
        }

        /// <summary>指定命名相机是否为 Brain 当前活动；未初始化 / <c>null</c> / 未知 id 返回 <c>false</c>。</summary>
        /// <param name="cameraId">命名相机 id。</param>
        public static bool IsActive(string cameraId)
        {
            if (cameraId == null || _impl == null)
                return false;

            return _impl.TryGetHandle(cameraId, out var handle) && _impl.IsActive(handle);
        }

        #endregion

        #region Public API — Registry

        /// <summary>注册一台命名相机，返回句柄；注册不激活、不改变场景状态。</summary>
        /// <typeparam name="T">后端相机类型；由实现解释。</typeparam>
        /// <param name="cameraId">命名 id。</param>
        /// <param name="camera">后端相机对象。</param>
        /// <returns>相机句柄。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 或 <paramref name="camera"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException"><paramref name="camera"/> 是已销毁的 Unity 对象，或实现不接受该类型。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static CameraHandle Register<T>(string cameraId, T camera) where T : class
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            ValidateCameraArgument(camera, nameof(camera));
            EnsureInitialized();
            return _impl.Register(cameraId, camera);
        }

        /// <summary>注册一台动态相机，返回句柄；注册不激活、不改变场景状态。</summary>
        /// <typeparam name="T">后端相机类型；由实现解释。</typeparam>
        /// <param name="camera">后端相机对象。</param>
        /// <returns>相机句柄；<see cref="CameraHandle.NamedId"/> 为 <c>null</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="camera"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException"><paramref name="camera"/> 是已销毁的 Unity 对象，或实现不接受该类型。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static CameraHandle Register<T>(T camera) where T : class
        {
            ValidateCameraArgument(camera, nameof(camera));
            EnsureInitialized();
            return _impl.Register(camera);
        }

        /// <summary>注销一台相机；无效句柄或门面未初始化时安全返回 <c>false</c>。</summary>
        /// <param name="handle">注册时返回的句柄。</param>
        /// <returns>确实移除了才返回 <c>true</c>。</returns>
        public static bool Unregister(CameraHandle handle)
        {
            return _impl != null && _impl.Unregister(handle);
        }

        /// <summary>按命名 id 注销一台相机；未知 id 或门面未初始化时安全返回 <c>false</c>。</summary>
        /// <param name="cameraId">命名相机 id。</param>
        /// <returns>确实移除了才返回 <c>true</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        public static bool Unregister(string cameraId)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            if (_impl == null || !_impl.TryGetHandle(cameraId, out var handle))
                return false;

            return _impl.Unregister(handle);
        }

        #endregion

        #region Public API — Commands

        /// <summary>立即切换到指定句柄的相机（cut）。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static void Activate(CameraHandle handle)
        {
            EnsureInitialized();
            _impl.Activate(handle);
        }

        /// <summary>立即切换到指定命名相机（cut）；未知 id 告警并 no-op。</summary>
        /// <param name="cameraId">命名相机 id。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static void Activate(string cameraId)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            EnsureInitialized();
            if (!_impl.TryGetHandle(cameraId, out var handle))
            {
                WarnUnknownCameraId(cameraId);
                return;
            }

            _impl.Activate(handle);
        }

        /// <summary>反激活指定句柄的相机。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static void Deactivate(CameraHandle handle)
        {
            EnsureInitialized();
            _impl.Deactivate(handle);
        }

        /// <summary>反激活指定命名相机；未知 id 告警并 no-op。</summary>
        /// <param name="cameraId">命名相机 id。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static void Deactivate(string cameraId)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            EnsureInitialized();
            if (!_impl.TryGetHandle(cameraId, out var handle))
            {
                WarnUnknownCameraId(cameraId);
                return;
            }

            _impl.Deactivate(handle);
        }

        /// <summary>过渡到指定句柄的相机。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <param name="duration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static void TransitionTo(CameraHandle handle, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            EnsureInitialized();
            _impl.TransitionTo(handle, duration, style);
        }

        /// <summary>过渡到指定命名相机；未知 id 告警并 no-op。</summary>
        /// <param name="cameraId">命名相机 id。</param>
        /// <param name="duration">过渡时长（秒）；<c>&lt;= 0.0001</c> 等价于 cut；NaN / ±∞ 抛异常。</param>
        /// <param name="style">过渡样式。</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameraId"/> 为 <c>null</c>。</exception>
        /// <exception cref="InvalidOperationException">门面尚未初始化。</exception>
        public static void TransitionTo(string cameraId, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            if (cameraId == null)
                throw new ArgumentNullException(nameof(cameraId));

            EnsureInitialized();
            if (!_impl.TryGetHandle(cameraId, out var handle))
            {
                WarnUnknownCameraId(cameraId);
                return;
            }

            _impl.TransitionTo(handle, duration, style);
        }

        #endregion

        #region Lifecycle

        /// <summary>注入相机管理实现；门面取得所有权。</summary>
        /// <param name="impl">相机管理实现。</param>
        /// <exception cref="ArgumentNullException"><paramref name="impl"/> 为 <c>null</c>。</exception>
        /// <remarks>重复调用告警并保留第一个实现；<b>被拒绝的第二个实例仍归调用方所有</b>，请自行释放
        /// （Cinemachine 后端在构造时订阅静态事件，未释放会泄漏监听器）。</remarks>
        public static void Initialize(ICameraManager impl)
        {
            if (impl == null)
                throw new ArgumentNullException(nameof(impl));

            if (_impl != null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "CameraManager.Initialize 被重复调用，忽略重复注入；传入的实例未被接管，请自行释放。");
                return;
            }

            _impl = impl;
        }

        /// <summary>销毁门面并释放注入的实现（若它实现 <see cref="IDisposable"/>）。</summary>
        /// <remarks>先清槽再 Dispose：第三方 Dispose 抛出时不得留下「已销毁却仍被门面持有」的永久闩锁
        /// （CAM-002）；Dispose 异常继续向调用方传播。</remarks>
        public static void Destroy()
        {
            var impl = _impl;
            _impl = null;

            if (impl is IDisposable disposable)
                disposable.Dispose();
        }

        #endregion

        #region Session Reset

        /// <summary>
        /// 会话开始时复位静态状态：只丢引用、不 Dispose。
        /// <para>关闭域重载时上一会话的静态字段会存活；门面在正常游戏流程里不会被
        /// <see cref="Destroy"/>，不复位会让第二会话的 <see cref="Initialize"/> 因重复而告警早退。</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForNewSession()
        {
            _impl = null;
        }

        #endregion

        #region Internal

        private static void EnsureInitialized()
        {
            if (_impl == null)
            {
                throw new InvalidOperationException(
                    "[CameraManager] CameraManager 尚未初始化。请先调用 CameraManager.Initialize(ICameraManager) 完成初始化。");
            }
        }

        private static void ValidateCameraArgument<T>(T camera, string paramName) where T : class
        {
            if (camera == null)
                throw new ArgumentNullException(paramName);

            if (camera is UnityEngine.Object unityObject && unityObject == null)
            {
                throw new ArgumentException(
                    "[CameraManager] 相机对象已被销毁，不能注册。", paramName);
            }
        }

        private static void WarnUnknownCameraId(string cameraId)
        {
            LogManager.Warning(LogCategories.CameraManager, "未知相机 id: {0}", cameraId);
        }

        #endregion
    }
}
