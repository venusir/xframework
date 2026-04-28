namespace XFramework
{
    /// <summary>
    /// 可更新接口。节点实现此接口后，通过 <see cref="Updater"/> 自动管理更新。
    /// <para>LOD 等级决定更新频率：帧间隔 = 1 << LOD。</para>
    /// <para>LOD=0: 每帧更新 | LOD=1: 每2帧 | LOD=2: 每4帧 | LOD=3: 每8帧 | LOD=4: 每16帧 | LOD=5: 每32帧</para>
    /// </summary>
    public interface IUpdateable
    {
        /// <summary>
        /// LOD 等级，范围 0~5。
        /// <para>帧间隔 = 1 << LOD。等级越高，更新频率越低。</para>
        /// </summary>
        int LOD { get; }

        /// <summary>
        /// 更新回调。由 <see cref="Updater"/> 按 LOD 时间切片调度。
        /// </summary>
        /// <param name="deltaTime">距上次更新的时间差。</param>
        void OnUpdate(float deltaTime);
    }
}
