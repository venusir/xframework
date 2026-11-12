using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera;
using XFramework.XLog;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 游戏场景的相机接线：把场景里的 Cinemachine 相机注册进 <see cref="CameraManager"/>，
    /// 切到游戏相机并绑定跟随目标；场景卸载时销毁相机管理器。
    /// <para>Cinemachine 类型只允许出现在 Camera 模块、本示例的 Game 场景脚本里（示例是核心包之外的
    /// 使用方代码，不受模块边界测试约束，但仍按同样口径把 3D 接线收在一处）。</para>
    /// </summary>
    public sealed class GameSceneController : MonoBehaviour
    {
        [Tooltip("游戏相机（CinemachineCamera）。")]
        [SerializeField] private CinemachineCamera _gameCamera;

        [Tooltip("结算相机（本示例暂未切换，保留注册示例）。")]
        [SerializeField] private CinemachineCamera _resultCamera;

        [Tooltip("游戏相机的跟随目标。")]
        [SerializeField] private Transform _followTarget;

        #region Constants

        private const string GameCameraId = "game";
        private const string ResultCameraId = "result";

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            var brain = FindAnyObjectByType<CinemachineBrain>();
            if (brain == null)
            {
                LogManager.Error(IntegrationLogCategories.Flow,
                    "Game 场景找不到 CinemachineBrain；相机示例已跳过。");
                return;
            }

            if (!CameraManager.IsInitialized)
                CameraManager.Initialize(brain);

            if (_gameCamera != null)
                CameraManager.Register(GameCameraId, _gameCamera);
            if (_resultCamera != null)
                CameraManager.Register(ResultCameraId, _resultCamera);
            if (_followTarget != null)
                CameraManager.SetFollow(GameCameraId, _followTarget);

            CameraManager.SwitchTo(GameCameraId, 0.2f);
        }

        private void OnDestroy()
        {
            // 场景卸载时拆掉相机管理器；它只服务于游戏场景。
            if (CameraManager.IsInitialized)
                CameraManager.Destroy();
        }

        #endregion
    }
}
