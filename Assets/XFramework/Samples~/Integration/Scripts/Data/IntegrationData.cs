using System;
using XFramework.XData;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 示例的数据块：累计场次与各模式最佳分。
    /// <para>由 <c>DataManager.GetOrCreateBlock</c> 创建，随存档保存/恢复；结算时
    /// <c>MarkDirty</c> 并在退出或结算后 <c>SaveManager.SaveAsync</c>。</para>
    /// </summary>
    [Serializable]
    public sealed class IntegrationData : IDataBlock
    {
        /// <summary>累计游玩场次。</summary>
        public int Sessions;

        /// <summary>TapRush 最佳分。</summary>
        public int BestTapScore;

        /// <summary>OddOneOut 最佳分。</summary>
        public int BestOddScore;

        /// <summary>上次玩的模式 id。</summary>
        public string LastMode = string.Empty;

        /// <inheritdoc/>
        public string BlockName => nameof(IntegrationData);

        /// <inheritdoc/>
        public int DataVersion => 1;

        /// <inheritdoc/>
        public object OnSave() => this;

        /// <inheritdoc/>
        public object OnMigrate(object saveData, int fromVersion) => saveData;

        /// <inheritdoc/>
        public void OnLoad(object saveData)
        {
            if (saveData is IntegrationData data)
            {
                Sessions = data.Sessions;
                BestTapScore = data.BestTapScore;
                BestOddScore = data.BestOddScore;
                LastMode = data.LastMode;
            }
        }

        /// <inheritdoc/>
        public void OnClear()
        {
            Sessions = 0;
            BestTapScore = 0;
            BestOddScore = 0;
            LastMode = string.Empty;
        }
    }
}
