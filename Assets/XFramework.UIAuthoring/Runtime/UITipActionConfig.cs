using System;
using UnityEngine;

namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// <see cref="UIActionKind.ShowTip"/> 的序列化配置。
    /// <para>不能直接序列化 <c>TipConfig</c>：它含 <c>Vector3?</c>，Unity 不序列化可空值类型。</para>
    /// </summary>
    [Serializable]
    public sealed class UITipActionConfig
    {
        /// <summary>是否使用世界坐标位置。</summary>
        public bool UseWorldPos;

        /// <summary>世界坐标位置（<see cref="UseWorldPos"/> 为 true 时生效）。</summary>
        public Vector3 WorldPos;

        /// <summary>文字颜色。</summary>
        public Color Color = Color.white;

        /// <summary>显示时长（秒）。</summary>
        public float Duration = 2f;

        /// <summary>上飘距离（像素）。</summary>
        public float FloatDistance;

        /// <summary>字号；0 表示沿用预制体默认值。</summary>
        public float FontSize;

        /// <summary>转换为核心 <c>TipConfig</c>。</summary>
        public TipConfig ToTipConfig()
        {
            return new TipConfig
            {
                WorldPos = UseWorldPos ? WorldPos : (Vector3?)null,
                Color = Color,
                Duration = Duration,
                FloatDistance = FloatDistance,
                FontSize = FontSize,
            };
        }
    }
}
