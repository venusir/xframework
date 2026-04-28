namespace XFramework
{
    /// <summary>
    /// 可更新接口。节点实现此接口后，通过 <see cref="Updater"/> 自动管理更新。
    /// <para><see cref="OnUpdate(float)"/> 的返回值决定下一帧的 LOD 等级：</para>
    /// <para>LOD=0: 每帧更新 | LOD=1: 每2帧 | LOD=2: 每4帧 | LOD=3: 每8帧 | LOD=4: 每16帧 | LOD=5: 每32帧</para>
    /// </summary>
    public interface IUpdateable
    {
        /// <summary>
        /// 执行更新并返回下一帧的 LOD 等级。
        /// <para>返回值范围 0~5，超出会被 Clamp 到有效范围。</para>
        /// </summary>
        /// <param name="deltaTime">距上次更新的时间差。</param>
        /// <returns>下一帧的 LOD 等级。</returns>
        int OnUpdate(float deltaTime);
    }
}
