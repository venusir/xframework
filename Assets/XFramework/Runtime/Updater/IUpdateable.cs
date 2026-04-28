namespace XFramework
{
    /// <summary>
    /// 更新 LOD 等级，决定 <see cref="IUpdateable.OnUpdate(float)"/> 的调用频率。
    /// <para>等级越高，更新间隔越大，帧消耗越低。</para>
    /// </summary>
    public enum UpdateLOD
    {
        /// <summary>每帧更新（帧间隔 = 1 帧）</summary>
        EveryFrame = 0,

        /// <summary>每 2 帧更新一次</summary>
        Every2Frames = 1,

        /// <summary>每 4 帧更新一次</summary>
        Every4Frames = 2,

        /// <summary>每 8 帧更新一次</summary>
        Every8Frames = 3,

        /// <summary>每 16 帧更新一次</summary>
        Every16Frames = 4,

        /// <summary>每 32 帧更新一次</summary>
        Every32Frames = 5,

        /// <summary>最大 LOD 等级标记，用于 <see cref="Updater"/> 内部推导数组大小。</summary>
        Max = Every32Frames,
    }

    /// <summary>
    /// 可更新接口。节点实现此接口后，通过 <see cref="Updater"/> 自动管理更新。
    /// <para><see cref="OnUpdate(float, float)"/> 的返回值决定下一帧的 <see cref="UpdateLOD"/> 等级。</para>
    /// </summary>
    public interface IUpdateable
    {
        /// <summary>
        /// 执行更新并返回下一帧的 <see cref="UpdateLOD"/> 等级。
        /// </summary>
        /// <param name="deltaTime">距上次更新的时间差。</param>
        /// <param name="time">当前时间（<see cref="UnityEngine.Time.time"/>），可用于绝对时间计算。</param>
        /// <returns>下一帧的更新频率等级。</returns>
        UpdateLOD OnUpdate(float deltaTime, float time);
    }
}
