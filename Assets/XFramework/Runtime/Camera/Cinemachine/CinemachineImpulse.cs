using Unity.Cinemachine;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XCamera.Cinemachine
{
    /// <summary>
    /// 震屏辅助：在输出对象上惰性取 / 建 <see cref="CinemachineImpulseSource"/>，并检查活动相机是否带
    /// <see cref="CinemachineImpulseListener"/>。
    /// <para>后端自建的 source 在 Dispose 时销毁；复用的既有 source 不销毁。监听器缺失只告警一次，
    /// <b>不自动添加</b>——不静默改写项目场景。</para>
    /// </summary>
    internal sealed class CinemachineImpulse
    {
        private CinemachineImpulseSource _source;
        private bool _created;
        private bool _warnedMissingListener;

        /// <summary>触发一次震屏；参数由后端先行校验。</summary>
        public void Shake(CinemachineBrain brain, float strength, float duration)
        {
            if (brain == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "输出驱动已销毁，震屏已忽略。");
                return;
            }

            if (!EnsureSource(brain))
                return;

            var active = brain.ActiveVirtualCamera;
            if (active == null)
            {
                LogManager.Warning(LogCategories.CameraManager,
                    "当前没有活动相机，震屏已忽略。");
                return;
            }

            var vcam = active as CinemachineVirtualCameraBase;
            if (vcam is CinemachineCameraManagerBase manager &&
                manager.LiveChild is CinemachineVirtualCameraBase liveChild)
            {
                vcam = liveChild;
            }

            if (vcam == null || vcam.GetComponent<CinemachineImpulseListener>() == null)
            {
                if (!_warnedMissingListener)
                {
                    _warnedMissingListener = true;
                    LogManager.Warning(LogCategories.CameraManager,
                        "活动相机缺少 CinemachineImpulseListener，震屏不会生效；" +
                        "请把该组件加到活动相机的 vcam 上。");
                }
            }

            _source.ImpulseDefinition.ImpulseDuration = duration;
            _source.GenerateImpulseWithForce(strength);
        }

        /// <summary>销毁后端自建的 source；复用的既有 source 保留。</summary>
        public void Dispose()
        {
            if (_created && _source != null)
                Object.Destroy(_source);

            _source = null;
            _created = false;
        }

        private bool EnsureSource(CinemachineBrain brain)
        {
            if (_source != null)
                return true;

            // ControlledObject 才是输出对象；OutputCamera 可能为 null。
            var host = brain.ControlledObject != null ? brain.ControlledObject : brain.gameObject;
            _source = host.GetComponent<CinemachineImpulseSource>();
            if (_source == null)
            {
                _source = host.AddComponent<CinemachineImpulseSource>();
                _created = true;
            }

            return _source != null;
        }
    }
}
