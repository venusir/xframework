using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XCamera
{
    /// <summary>
    /// 相机管理的静态门面：注入后端实现，转发命名相机与动态相机的激活 / 反激活 / 过渡与查询。
    /// <para><b>为什么需要它</b>：游戏代码需要「切到哪台相机 / 何时过渡」，但不该直接依赖
    /// Cinemachine 的 vcam 与 Brain 类型。门面把这件事收敛成一个后端中立的入口；相机编排
    /// （跟随 / 瞄准 / 震屏 / 约束）仍归 Cinemachine，本模块只做状态与过渡。</para>
    /// <para><b>所有权</b>：<see cref="Initialize"/> 后门面拥有实现，<see cref="Destroy"/> 会释放它
    /// （若实现 <see cref="IDisposable"/>）。<see cref="ResetForNewSession"/> 只丢引用、不 Dispose。</para>
    /// <example>
    /// <code>
    /// CameraManager.Initialize(new UnityCameraManager(cameras));
    /// CameraManager.Activate("main");
    /// CameraManager.TransitionTo("battle", 0.8f, CameraBlendStyle.EaseInOut);
    /// var camera = CameraManager.OutputCamera; // 世界↔屏幕、射线直接用 UnityEngine.Camera
    ///
    /// // 随预制创建 / 销毁的动态相机：用句柄，不生成 string id
    /// var handle = CameraManager.Register(prefabCamera);
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

        #region Public API

        /// <summary>当前是否有已注入的实现。</summary>
        public static bool IsInitialized => _impl != null;

        /// <summary>当前输出相机；无活动相机时为 <c>null</c>。</summary>
        public static Camera OutputCamera
        {
            get
            {
                EnsureInitialized();
                return _impl.OutputCamera;
            }
        }

        /// <summary>已注册的相机 id（主线程实时视图）。</summary>
        public static IReadOnlyCollection<string> CameraIds
        {
            get
            {
                EnsureInitialized();
                return _impl.CameraIds;
            }
        }

        /// <summary>当前活动相机的命名 id；过渡期间是目标 id；动态相机活动或无活动相机时为 <c>null</c>。</summary>
        /// <remarks>= <see cref="ActiveHandle"/>.<see cref="CameraHandle.NamedId"/>（O(1)、零遍历）；
        /// 动态相机没有命名 id。</remarks>
        public static string ActiveCameraId
        {
            get
            {
                EnsureInitialized();
                return _impl.ActiveHandle.NamedId;
            }
        }

        /// <summary>是否有相机过渡在途。</summary>
        public static bool IsTransitioning
        {
            get
            {
                EnsureInitialized();
                return _impl.IsTransitioning;
            }
        }

        /// <summary>当前活动相机的句柄（命名与动态都有）；过渡期间是目标；无活动为 <c>default</c>。</summary>
        public static CameraHandle ActiveHandle
        {
            get
            {
                EnsureInitialized();
                return _impl.ActiveHandle;
            }
        }

        /// <summary>立即切换到指定相机（cut）。</summary>
        /// <param name="cameraId">已注册的相机 id。</param>
        public static void Activate(string cameraId)
        {
            EnsureInitialized();
            _impl.Activate(cameraId);
        }

        /// <summary>反激活指定相机。</summary>
        /// <param name="cameraId">已注册的相机 id。</param>
        public static void Deactivate(string cameraId)
        {
            EnsureInitialized();
            _impl.Deactivate(cameraId);
        }

        /// <summary>过渡到指定相机。</summary>
        /// <param name="cameraId">已注册的相机 id。</param>
        /// <param name="duration">过渡时长（秒）；<c>&lt;= 0</c> 等价于 cut。</param>
        /// <param name="style">过渡样式。</param>
        public static void TransitionTo(string cameraId, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            EnsureInitialized();
            _impl.TransitionTo(cameraId, duration, style);
        }

        /// <summary>指定相机是否为当前活动（过渡期间的目标也算活动）。</summary>
        /// <param name="cameraId">相机 id。</param>
        public static bool IsActive(string cameraId)
        {
            EnsureInitialized();
            return _impl.IsActive(cameraId);
        }

        /// <summary>注册一台动态相机，返回其后端句柄。</summary>
        /// <typeparam name="T">后端相机类型；由实现解释（UnityCameraManager 要 <see cref="Camera"/>，
        /// Cinemachine 实现要它的 vcam 基类）。</typeparam>
        /// <param name="handle">后端相机对象。</param>
        /// <returns>动态相机句柄。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handle"/> 为 <c>null</c>。</exception>
        public static CameraHandle Register<T>(T handle) where T : class
        {
            if (handle == null)
                throw new ArgumentNullException(nameof(handle));

            EnsureInitialized();
            return _impl.Register(handle);
        }

        /// <summary>注销一台动态相机。</summary>
        /// <param name="handle">寄存器返回的句柄。</param>
        /// <returns>确实移除了才返回 <c>true</c>；门面未初始化 / 已销毁时安全返回 <c>false</c>。</returns>
        public static bool Unregister(CameraHandle handle)
        {
            return _impl != null && _impl.Unregister(handle);
        }

        /// <summary>立即切换到指定句柄的相机（cut）。</summary>
        /// <param name="handle">相机句柄。</param>
        public static void Activate(CameraHandle handle)
        {
            EnsureInitialized();
            _impl.Activate(handle);
        }

        /// <summary>反激活指定句柄的相机。</summary>
        /// <param name="handle">相机句柄。</param>
        public static void Deactivate(CameraHandle handle)
        {
            EnsureInitialized();
            _impl.Deactivate(handle);
        }

        /// <summary>过渡到指定句柄的相机。</summary>
        /// <param name="handle">相机句柄。</param>
        /// <param name="duration">过渡时长（秒）；<c>&lt;= 0</c> 等价于 cut。</param>
        /// <param name="style">过渡样式。</param>
        public static void TransitionTo(CameraHandle handle, float duration,
            CameraBlendStyle style = CameraBlendStyle.EaseInOut)
        {
            EnsureInitialized();
            _impl.TransitionTo(handle, duration, style);
        }

        /// <summary>指定句柄的相机是否为当前活动（过渡期间的目标也算活动）。</summary>
        /// <param name="handle">相机句柄。</param>
        public static bool IsActive(CameraHandle handle)
        {
            EnsureInitialized();
            return _impl.IsActive(handle);
        }

        #endregion

        #region Lifecycle

        /// <summary>注入相机管理实现；门面取得所有权。</summary>
        /// <param name="impl">相机管理实现。</param>
        /// <exception cref="ArgumentNullException"><paramref name="impl"/> 为 <c>null</c>。</exception>
        public static void Initialize(ICameraManager impl)
        {
            if (impl == null)
                throw new ArgumentNullException(nameof(impl));

            if (_impl != null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "CameraManager.Initialize 被重复调用，忽略重复注入。");
                return;
            }

            _impl = impl;
        }

        /// <summary>销毁门面并释放注入的实现（若它实现 <see cref="IDisposable"/>）。</summary>
        public static void Destroy()
        {
            if (_impl is IDisposable disposable)
                disposable.Dispose();

            _impl = null;
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

        #endregion
    }
}
