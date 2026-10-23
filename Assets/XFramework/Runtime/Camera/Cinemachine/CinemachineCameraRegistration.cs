using Unity.Cinemachine;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XCamera.Cinemachine
{
    /// <summary>
    /// 预制体 / 动态相机自注册组件：把同物体（或序列化指定）的 vcam 注册进
    /// <see cref="CameraManager"/>，并在对象销毁时自动注销。
    /// <para><b>为什么需要它</b>：预制体在实例化前不存在，无法用 authoring id 预注册；同一预制体
    /// 实例化多次还会产生多份相机。组件在启用时拿一个运行时生成的 <c>@auto/N</c> id，天然按实例隔离。</para>
    /// <para><b>生命周期</b>：<see cref="OnEnable"/> 在管理器已初始化时注册；<see cref="Start"/> 再重试一次，
    /// 覆盖组合根在 Awake / Start 里初始化后端的时序；仍未初始化则告警，调用方可在稍后显式
    /// <see cref="Register"/>。<see cref="OnDestroy"/> 自动 <see cref="Unregister"/>；禁用不注销（池化友好）。</para>
    /// <para><b>只注册父相机</b>：ClearShot / MixingCamera 等分组相机请把组件挂在父相机上，子相机不要挂。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("XFramework/Camera/Cinemachine Camera Registration")]
    public sealed class CinemachineCameraRegistration : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private CinemachineVirtualCameraBase _camera;
        [SerializeField] private bool _registerOnEnable = true;

        #endregion

        #region Private Fields

        private bool _warnedMissingManager;
        private bool _warnedRegisterFailure;

        #endregion

        #region Public API

        /// <summary>本组件注册的 vcam；未指定时取同物体上的 vcam。</summary>
        public CinemachineVirtualCameraBase Camera
        {
            get
            {
                ResolveCamera();
                return _camera;
            }
        }

        /// <summary>注册后由管理器返回的 id；未注册时为 <c>null</c>。</summary>
        public string CameraId { get; private set; }

        /// <summary>当前是否仍以 <see cref="CameraId"/> 注册着本组件对应的 vcam。</summary>
        public bool IsRegistered
        {
            get
            {
                if (CameraId == null || !CameraManager.IsInitialized)
                    return false;

                return CameraManager.TryGetCamera(CameraId, out CinemachineVirtualCameraBase current) &&
                       ReferenceEquals(current, _camera);
            }
        }

        /// <summary>注册本组件对应的 vcam；幂等返回已注册的 id。</summary>
        /// <returns>相机 id。</returns>
        /// <exception cref="System.InvalidOperationException">找不到 vcam，或管理器尚未初始化。</exception>
        /// <exception cref="System.ArgumentException">同一相机已注册为另一 id。</exception>
        public string Register()
        {
            ResolveCamera();
            if (_camera == null)
            {
                throw new System.InvalidOperationException(
                    "[Camera] CinemachineCameraRegistration 所在物体上没有 CinemachineVirtualCameraBase。");
            }

            if (CameraId != null && IsRegistered)
                return CameraId;

            CameraId = CameraManager.Register(_camera);
            return CameraId;
        }

        /// <summary>注销本组件对应的相机；未注册或管理器已销毁时安全返回。</summary>
        public void Unregister()
        {
            if (CameraId == null)
                return;

            if (CameraManager.IsInitialized)
                CameraManager.Unregister(CameraId);

            CameraId = null;
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            ResolveCamera();
        }

        private void OnEnable()
        {
            if (_registerOnEnable)
                TryRegister(verbose: false);
        }

        private void Start()
        {
            if (_registerOnEnable && CameraId == null)
                TryRegister(verbose: true);
        }

        private void OnDestroy()
        {
            Unregister();
        }

        private void Reset()
        {
            _camera = GetComponent<CinemachineVirtualCameraBase>();
        }

        #endregion

        #region Internal

        private void ResolveCamera()
        {
            if (_camera == null)
                _camera = GetComponent<CinemachineVirtualCameraBase>();
        }

        private bool TryRegister(bool verbose)
        {
            if (!CameraManager.IsInitialized)
            {
                if (verbose)
                {
                    WarnOnce(ref _warnedMissingManager,
                        "[Camera] CinemachineCameraRegistration 无法自动注册：CameraManager 尚未初始化；" +
                        "请先初始化相机后端，或稍后显式调用 Register()。");
                }

                return false;
            }

            try
            {
                Register();
                return true;
            }
            catch (System.Exception exception) when (exception is System.ArgumentException ||
                                                     exception is System.InvalidOperationException)
            {
                WarnOnce(ref _warnedRegisterFailure,
                    "[Camera] CinemachineCameraRegistration 自动注册失败，可稍后显式调用 Register()：" +
                    exception.Message);
                return false;
            }
        }

        private static void WarnOnce(ref bool flag, string message)
        {
            if (flag)
                return;

            flag = true;
            LogManager.Warning(LogCategories.CameraManager, message);
        }

        #endregion
    }
}
