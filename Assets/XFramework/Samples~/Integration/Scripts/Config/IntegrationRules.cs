using System;

namespace XFramework.Samples.Integration
{
    /// <summary>示例的全局规则（Config 模块的 Global JSON 对象）。</summary>
    [Serializable]
    public sealed class IntegrationRules
    {
        /// <summary>默认单局时长（秒）。</summary>
        public float DefaultDuration = 30f;

        /// <summary>默认目标/选项数量。</summary>
        public int TargetCount = 6;

        /// <summary>默认回合数。</summary>
        public int Rounds = 5;

        /// <summary>每次命中得分。</summary>
        public int ScorePerHit = 1;
    }
}
