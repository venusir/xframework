using TMPro;
using UnityEngine;

namespace XFramework.XUI
{
    /// <summary>
    /// 通用 Tip 显示组件。挂载在预制体 PF_UITipText 上。
    /// <para>由 <see cref="UITipManagerImpl"/> 管理生命周期：<see cref="Begin"/> 设置内容，
    /// 之后由管理器在每帧通路里调用 <see cref="Tick"/> 推进动画，播完由管理器回池。</para>
    /// <para><b>不自行驱动帧</b>：早先本类跑一个 <c>UniTask.Yield</c> 自循环并读 <c>Time.deltaTime</c>，
    /// 于是 Tip 既不受 <c>UpdateManager.Pause</c> 约束、也不进档位调度——那是 UI 模块内最后一条
    /// 绕过统一调度的帧通路。</para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UITipItem : MonoBehaviour
    {
        #region Fields

        private CanvasGroup _canvasGroup;
        private TMP_Text _tmpText;
        private RectTransform _rectTransform;
        private Camera _camera;

        private float _elapsed;
        private float _duration;
        private Vector3 _startScreenPos;
        private Vector3 _endScreenPos;
        private bool _playing;

        #endregion

        #region Properties

        /// <summary>是否正在播放。</summary>
        public bool IsPlaying => _playing;

        private CanvasGroup CanvasGroup
        {
            get
            {
                if (_canvasGroup == null)
                    _canvasGroup = GetComponent<CanvasGroup>();

                return _canvasGroup;
            }
        }

        private TMP_Text TmpText
        {
            get
            {
                if (_tmpText == null)
                    _tmpText = GetComponentInChildren<TMP_Text>(true);

                return _tmpText;
            }
        }

        private RectTransform RectTransform
        {
            get
            {
                if (_rectTransform == null)
                    _rectTransform = (RectTransform)transform;

                return _rectTransform;
            }
        }

        private Camera Camera
        {
            get
            {
                if (_camera == null)
                    _camera = Camera.main;

                return _camera;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 开始播放：设置文字与显示参数。之后需由管理器逐帧调用 <see cref="Tick"/>。
        /// </summary>
        /// <param name="text">显示文字。</param>
        /// <param name="config">显示配置。</param>
        public void Begin(string text, TipConfig config)
        {
            if (TmpText == null)
            {
                Debug.LogError("[UITipItem] TMP_Text component not found on prefab.");
                return;
            }

            _duration = Mathf.Max(0.01f, config.Duration);
            _elapsed = 0f;

            TmpText.text = text;
            TmpText.color = config.Color;

            // 字号 0 表示沿用预制体默认值
            if (config.FontSize > 0f)
                TmpText.fontSize = config.FontSize;

            _startScreenPos = ResolveStartPosition(config);
            _endScreenPos = _startScreenPos + Vector3.up * config.FloatDistance;

            RectTransform.position = _startScreenPos;
            CanvasGroup.alpha = 1f;
            CanvasGroup.blocksRaycasts = false;
            gameObject.SetActive(true);

            _playing = true;
        }

        /// <summary>
        /// 推进一帧。
        /// </summary>
        /// <param name="deltaTime">距上次派发的间隔。由驱动方给出，<b>不是 <c>Time.deltaTime</c></b>
        /// ——Tip 随面板一同受档位与统一暂停调度，两者可能相差若干倍。</param>
        /// <returns>播放完毕返回 true，此时管理器应回收本实例。</returns>
        public bool Tick(float deltaTime)
        {
            if (!_playing)
                return true;

            _elapsed += deltaTime;

            var t = Mathf.Clamp01(_elapsed / _duration);
            RectTransform.position = Vector3.Lerp(_startScreenPos, _endScreenPos, t);

            // 渐隐：前半程保持不透明，后半程渐隐
            var fadeT = Mathf.Clamp01((t - 0.5f) / 0.5f);
            CanvasGroup.alpha = 1f - fadeT;

            return _elapsed >= _duration;
        }

        /// <summary>
        /// 立即结束播放并隐藏。取消或回池前调用。
        /// </summary>
        public void StopImmediate()
        {
            _playing = false;
            gameObject.SetActive(false);
        }

        #endregion

        #region Private

        /// <summary>
        /// 解析起始屏幕位置：给了世界坐标就投影，否则屏幕居中。
        /// </summary>
        private Vector3 ResolveStartPosition(TipConfig config)
        {
            if (config.WorldPos.HasValue && Camera != null)
                return Camera.WorldToScreenPoint(config.WorldPos.Value);

            return new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        }

        #endregion
    }
}
