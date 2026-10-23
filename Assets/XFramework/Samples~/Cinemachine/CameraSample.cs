using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;
using XFramework.XCamera;

namespace XFramework.XCamera.Cinemachine.Samples
{
    /// <summary>
    /// 示例：把 <see cref="CinemachineBrain"/> 与两个 vcam 接进 <see cref="CameraManager"/>，
    /// 演示基础相机切换、临时覆盖、跟随绑定、震屏、异步等待与原生逃生舱。
    /// </summary>
    public sealed class CameraSample : MonoBehaviour
    {
        [SerializeField] private CinemachineBrain _brain;
        [SerializeField] private CinemachineCamera _first;
        [SerializeField] private CinemachineCamera _second;
        [SerializeField] private Transform _follow;

        private void Awake()
        {
            CameraManager.Initialize(_brain);
            CameraManager.Register("first", _first);
            CameraManager.Register("second", _second);
            CameraManager.SwitchTo("first");

            if (_follow != null)
                CameraManager.SetFollow("first", _follow);
        }

        private void OnDestroy()
        {
            CameraManager.Destroy();
        }

        /// <summary>在基础相机之间切换；可接到按钮 / 输入回调。</summary>
        public void SwitchToNext()
        {
            var next = CameraManager.ActiveCameraId == "first" ? "second" : "first";
            CameraManager.SwitchTo(next, 0.8f, CameraBlendStyle.EaseInOut);
        }

        /// <summary>异步切换并等待完成；取消令牌只停止等待，不中止过渡。</summary>
        public async UniTask<CameraTransitionResult> SwitchToNextAsync(
            CancellationToken cancellationToken = default)
        {
            var next = CameraManager.ActiveCameraId == "first" ? "second" : "first";
            return await CameraManager.SwitchToAsync(
                next, 0.8f, CameraBlendStyle.EaseInOut, cancellationToken);
        }

        /// <summary>临时覆盖到第二台相机；<see cref="PopSecond"/> 返回。</summary>
        public void PushSecond()
        {
            CameraManager.Push("second", 0.2f);
        }

        /// <summary>返回上一个相机。</summary>
        public void PopSecond()
        {
            CameraManager.Pop(0.25f);
        }

        /// <summary>触发一次震屏；活动相机需要带 CinemachineImpulseListener。</summary>
        public void Shake()
        {
            CameraManager.Shake(0.4f);
        }

        /// <summary>高级访问：需要配置 Lens / Aim / Confiner 时取回 vcam 本身。</summary>
        public bool TryGetSecond(out CinemachineVirtualCameraBase camera)
        {
            return CameraManager.TryGetCamera("second", out camera);
        }
    }
}
