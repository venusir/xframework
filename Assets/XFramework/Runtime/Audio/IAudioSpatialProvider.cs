using UnityEngine;

namespace XFramework.XAudio
{
    /// <summary>
    /// 播放引擎的<b>逐次 3D 位置能力</b>。可选能力接口：provider 不实现时，
    /// <see cref="AudioManager.SetPosition(AudioHandle, Vector3)"/> 返回 <c>false</c>。
    /// <para><b>官方扩展能力接口</b>：本类型为 internal，经 <c>InternalsVisibleTo</c> 提供给官方 FMOD
    /// 扩展程序集；它不是第三方可自由实现的公开契约。</para>
    /// <para><b>线程</b>：限主线程调用。</para>
    /// </summary>
    internal interface IAudioSpatialProvider
    {
        /// <summary>
        /// 更新一次播放的 3D 世界坐标。返回 <c>false</c> 表示句柄已失效或播放源已被销毁。
        /// <para>纯 2D 播放也可以成功写入位置——公开承诺是「把位置写到播放源」，不是「一定产生空间化效果」。</para>
        /// </summary>
        /// <param name="handle">provider 句柄。</param>
        /// <param name="position">世界坐标。</param>
        /// <returns>是否已应用。</returns>
        bool TrySetPosition(AudioProviderHandle handle, Vector3 position);
    }
}
