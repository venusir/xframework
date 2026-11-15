using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XCamera;

namespace XFramework.XCamera.Cinemachine.Samples
{
    /// <summary>
    /// Cinemachine 示例的 IMGUI 操作面板：把 <see cref="CameraSample"/> 的公开方法接到按钮上，
    /// 并显示活动相机 id、栈深度与混合状态。
    /// <para>它只是示例外壳：真实项目应把这些调用接到自己的 UI / 输入系统上。</para>
    /// </summary>
    public sealed class CinemachineSampleOverlay : MonoBehaviour
    {
        #region Inspector

        [SerializeField] private CameraSample _sample;
        [SerializeField] private Transform _target;

        #endregion

        #region Private Fields

        private GUIStyle _richLabel;
        private bool _spin;
        private string _asyncStatus = string.Empty;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            // 场景生成器会写好引用；手挂时按类型找一下，少一步 Inspector 操作。
            if (_sample == null)
                _sample = FindAnyObjectByType<CameraSample>();
        }

        private void Update()
        {
            if (_spin && _target != null)
                _target.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 460f, 330f), GUI.skin.box);
            GUILayout.Label("<b>Cinemachine Camera Sample</b>", RichLabel());
            GUILayout.Label(
                $"initialized={CameraManager.IsInitialized}  " +
                $"active={CameraManager.ActiveCameraId ?? "(none)"}  " +
                $"depth={CameraManager.StackDepth}  blending={CameraManager.IsBlending}",
                RichLabel());
            GUILayout.Space(6f);

            GUI.enabled = _sample != null;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Switch（0.8s EaseInOut）"))
                _sample.SwitchToNext();
            if (GUILayout.Button("Switch Async"))
                SwitchAsync().Forget();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Push Second（0.2s）"))
                _sample.PushSecond();
            if (GUILayout.Button("Pop（0.25s）"))
                _sample.PopSecond();
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Shake"))
                _sample.Shake();

            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_asyncStatus))
                GUILayout.Label(_asyncStatus, RichLabel());

            _spin = GUILayout.Toggle(_spin, "Spin Target（让 Follow 的效果可见）");
            GUILayout.EndArea();
        }

        #endregion

        #region Async Switch

        private async UniTaskVoid SwitchAsync()
        {
            _asyncStatus = "SwitchToAsync：等待混合完成…";

            try
            {
                var result = await _sample.SwitchToNextAsync(destroyCancellationToken);
                _asyncStatus = $"SwitchToAsync：{result}";
            }
            catch (OperationCanceledException)
            {
                // 对象销毁：只停止等待，不中止已经开始的相机过渡。
                _asyncStatus = "SwitchToAsync：等待已取消";
            }
        }

        #endregion

        #region Helpers

        private GUIStyle RichLabel()
        {
            // OnGUI 每帧会走 Layout/Repaint 多次；皮肤样式只建一次。
            return _richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
        }

        #endregion
    }
}
