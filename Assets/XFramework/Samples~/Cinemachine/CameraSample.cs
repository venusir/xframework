using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera;

namespace XFramework.XCamera.Cinemachine.Samples
{
    /// <summary>
    /// 示例：把 <see cref="CinemachineBrain"/> 与两个 vcam 接进 <see cref="CameraManager"/>，
    /// 用 <see cref="SwitchToNext"/>（可接到按钮 / 输入回调）在两台相机之间过渡。
    /// </summary>
    public sealed class CameraSample : MonoBehaviour
    {
        [SerializeField] private CinemachineBrain _brain;
        [SerializeField] private CinemachineCamera _first;
        [SerializeField] private CinemachineCamera _second;

        private void Awake()
        {
            var manager = new CinemachineCameraManager(_brain,
                new Dictionary<string, CinemachineVirtualCameraBase>
                {
                    ["first"] = _first,
                    ["second"] = _second,
                });

            CameraManager.Initialize(manager);
            CameraManager.Activate("first");
        }

        private void OnDestroy()
        {
            CameraManager.Destroy();
        }

        /// <summary>切到另一台相机；可在按钮或输入回调里调用。</summary>
        public void SwitchToNext()
        {
            var next = CameraManager.IsActive("first") ? "second" : "first";
            CameraManager.TransitionTo(next, 0.8f, CameraBlendStyle.EaseInOut);
        }
    }
}
