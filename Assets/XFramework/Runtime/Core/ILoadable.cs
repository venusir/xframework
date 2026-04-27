using Cysharp.Threading.Tasks;

namespace XFramework
{
    /// <summary>
    /// 可加载接口。实现此接口的节点在 <see cref="BaseNode.Start"/> 之前会先执行加载流程。
    /// <para>由 <see cref="GameLauncher"/> 在启动时统一收集并等待所有 <see cref="ILoadable"/> 节点加载完成后，再调用 <see cref="BaseNode.Start"/>。</para>
    /// </summary>
    public interface ILoadable
    {
        /// <summary>
        /// 当前加载进度，取值范围 0.0 ~ 1.0。
        /// <para>由 <see cref="GameLauncher"/> 每帧轮询以计算整体加载进度。</para>
        /// </summary>
        float Progress { get; }

        /// <summary>
        /// 当前加载阶段的描述文字，用于 UI 显示。
        /// <para>例如 "正在加载配置..."、"正在初始化资源..."。</para>
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 异步加载任务。在节点 Awake 完成后、Start 之前调用。
        /// <para>加载过程中应持续更新 <see cref="Progress"/> 属性。</para>
        /// </summary>
        /// <returns>加载任务。</returns>
        UniTask LoadAsync();
    }
}
