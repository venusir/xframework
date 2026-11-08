namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// <see cref="UIActionKind.Custom"/> 发布的 keyed 消息。
    /// <para>不带面板引用：<c>MessageManager</c> 的 keyed 发布会把最后一条写入重放缓存，
    /// 携带面板对象会让已关闭/回池的实例被缓存持有。需要面板上下文时由项目自行取。</para>
    /// </summary>
    public readonly struct UIAuthoringActionMessage
    {
        /// <summary>动作 ID（发布时的 key，与字段一致便于订阅方使用）。</summary>
        public readonly string ActionId;

        /// <summary>自定义负载。</summary>
        public readonly string Payload;

        /// <summary>创建一条自定义动作消息。</summary>
        public UIAuthoringActionMessage(string actionId, string payload)
        {
            ActionId = actionId;
            Payload = payload;
        }
    }
}
