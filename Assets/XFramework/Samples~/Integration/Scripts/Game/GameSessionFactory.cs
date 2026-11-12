namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 按模式 id 创建会话。第二个模式（OddOneOut）在后续步骤接入；未知 id 返回 null。
    /// <para>工厂刻意保持极简：示例要展示的是框架模块的接线，不是游戏侧的对象工厂设计。</para>
    /// </summary>
    public static class GameSessionFactory
    {
        /// <summary>创建会话；未知模式返回 null。</summary>
        public static IIntegrationSession Create(string modeId)
        {
            switch (modeId)
            {
                case TapRushSession.ModeIdValue:
                    return new TapRushSession();
                default:
                    return null;
            }
        }
    }
}
