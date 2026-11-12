using System;
using XFramework.XConfig;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 模式表行（Config 模块的 JSON 表；<c>{"Items":[...]}</c>）。
    /// <para>主键必须是<b>字段</b>：JsonUtility 不序列化属性；接口属性用显式实现读字段。</para>
    /// </summary>
    [Serializable]
    public struct GameModeRow : IConfigRow<string>
    {
        /// <summary>模式 id（表主键）。</summary>
        public string Id;

        /// <summary>显示名本地化键。</summary>
        public string DisplayKey;

        /// <summary>单局时长（秒）。</summary>
        public float Duration;

        /// <summary>目标/选项数量。</summary>
        public int TargetCount;

        /// <summary>回合数（OddOneOut 用；TapRush 忽略）。</summary>
        public int Rounds;

        /// <inheritdoc/>
        string IConfigRow<string>.Id => Id;
    }
}
