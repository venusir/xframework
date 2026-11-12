using System;
using UnityEngine;
using UnityEngine.UI;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 玩法面板的棋盘视图：把一组目标按钮包装成「下标 → 点击」的回调。
    /// <para>会话只关心「第几个目标」，不直接碰 uGUI；面板负责把按钮传进来。</para>
    /// </summary>
    public sealed class GameBoardView : IDisposable
    {
        #region Fields

        private readonly Button[] _targets;
        private readonly Text[] _labels;
        private readonly UnityEngine.Events.UnityAction[] _handlers;

        #endregion

        /// <summary>构造棋盘视图。</summary>
        /// <param name="targets">目标按钮（数量即目标数）。</param>
        /// <param name="labels">与按钮一一对应的文本。</param>
        /// <param name="onClick">点击回调，参数为目标下标。</param>
        public GameBoardView(Button[] targets, Text[] labels, Action<int> onClick)
        {
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            _labels = labels;
            _handlers = new UnityEngine.Events.UnityAction[_targets.Length];

            for (int i = 0; i < _targets.Length; i++)
            {
                int index = i;
                _handlers[i] = () => onClick?.Invoke(index);
                if (_targets[i] != null)
                    _targets[i].onClick.AddListener(_handlers[i]);
            }
        }

        /// <summary>目标数量。</summary>
        public int TargetCount => _targets.Length;

        /// <summary>显示 / 隐藏一个目标。</summary>
        public void SetTargetVisible(int index, bool visible)
        {
            if (index < 0 || index >= _targets.Length || _targets[index] == null)
                return;

            _targets[index].gameObject.SetActive(visible);
        }

        /// <summary>设置一个目标的文本。</summary>
        public void SetTargetLabel(int index, string text)
        {
            if (_labels == null || index < 0 || index >= _labels.Length || _labels[index] == null)
                return;

            _labels[index].text = text;
        }

        /// <summary>设置一个目标的背景色（OddOneOut 用）。</summary>
        public void SetTargetColor(int index, Color color)
        {
            if (index < 0 || index >= _targets.Length || _targets[index] == null)
                return;

            if (_targets[index].targetGraphic is Image image)
                image.color = color;
        }

        /// <summary>隐藏全部目标。</summary>
        public void HideAll()
        {
            for (int i = 0; i < _targets.Length; i++)
                SetTargetVisible(i, false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                if (_targets[i] != null && _handlers[i] != null)
                    _targets[i].onClick.RemoveListener(_handlers[i]);
            }
        }
    }
}
