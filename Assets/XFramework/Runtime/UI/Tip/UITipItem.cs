using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace XFramework.XUI
{
    /// <summary>
    /// 通用 Tip 显示组件。挂载在预制体 PF_UITipText 上。
    /// <para>由 <see cref="UITipManager"/> 统一管理生命周期，通过 <see cref="PlayAsync"/> 驱动显示和动画。</para>
    /// <para>动画结束后 <see cref="UITipManager"/> 负责调用 <see cref="XAsset.AssetManager.DestroyInstance"/> 回池。</para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UITipItem : MonoBehaviour
    {
        #region Fields

        private CanvasGroup _canvasGroup;
        private TMP_Text _tmpText;
        private RectTransform _rectTransform;
        private Camera _camera;
        private CancellationTokenSource _cts;

        #endregion

        #region Properties

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
        /// 异步播放 Tip 动画。
        /// <para>设置文字内容和显示参数，在异步循环中驱动动画帧。完成后返回。</para>
        /// </summary>
        /// <param name="text">显示文字。</param>
        /// <param name="config">显示配置。</param>
        /// <param name="cancellationToken">外部取消令牌，用于在场景切换等场景提前终止。</param>
        public async UniTask PlayAsync(string text, TipConfig config, CancellationToken cancellationToken = default)
        {
            if (TmpText == null)
            {
                Debug.LogError("[UITipItem] TMP_Text component not found on prefab.");
                return;
            }

            // 取消之前的动画
            StopImmediate();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var duration = Mathf.Max(0.01f, config.Duration);

            // 设置文字
            TmpText.text = text;
            TmpText.color = config.Color;

            // 设置字号（0 表示使用默认）
            if (config.FontSize > 0f)
                TmpText.fontSize = config.FontSize;

            // 确定起始/结束位置
            Vector3 startScreenPos;
            if (config.WorldPos.HasValue)
            {
                if (Camera != null)
                {
                    startScreenPos = Camera.WorldToScreenPoint(config.WorldPos.Value);
                }
                else
                {
                    startScreenPos = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
                }
            }
            else
            {
                startScreenPos = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            }

            var endScreenPos = startScreenPos + Vector3.up * config.FloatDistance;
            RectTransform.position = startScreenPos;

            CanvasGroup.alpha = 1f;
            CanvasGroup.blocksRaycasts = false;
            gameObject.SetActive(true);

            try
            {
                var elapsed = 0f;
                while (elapsed < duration)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    elapsed += Time.deltaTime;
                    var t = Mathf.Clamp01(elapsed / duration);

                    // 插值位置
                    RectTransform.position = Vector3.Lerp(startScreenPos, endScreenPos, t);

                    // 渐隐（前半程保持不透明，后半程渐隐）
                    var fadeT = Mathf.Clamp01((t - 0.5f) / 0.5f);
                    CanvasGroup.alpha = 1f - fadeT;

                    await UniTask.Yield(PlayerLoopTiming.Update, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 静默处理取消
            }
            finally
            {
                gameObject.SetActive(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// 立即取消当前动画。
        /// </summary>
        public void StopImmediate()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        #endregion

        #region Lifecycle

        private void OnDestroy()
        {
            StopImmediate();
        }

        #endregion
    }
}