using System;
using UnityEngine;
using XFramework.XAudio;

namespace XFramework.XAudio.Fmod
{
    /// <summary>
    /// provider 与真实 FMOD 后端之间的 FMOD-free 缝。
    /// <para><b>用途</b>：provider 只依赖本接口的 <c>int</c> 句柄与基础类型，干净克隆（无 FMOD）也能编译、
    /// 能用 fake backend 测试；真实实现位于条件集成程序集。</para>
    /// <para><b>线程</b>：全部成员限主线程调用。</para>
    /// <para><b>约定</b>：<see cref="Create(string)"/> 失败返回 <c>0</c>；<see cref="Release(int)"/> 幂等；
    /// 返回 <c>false</c> 表示实例已失效或操作未被接受。</para>
    /// </summary>
    internal interface IFmodEventBackend : IDisposable
    {
        /// <summary>创建一个事件实例；失败返回 <c>0</c>。</summary>
        int Create(string eventPath);

        /// <summary>启动事件。</summary>
        bool Start(int instanceId);

        /// <summary>停止事件（不释放）。</summary>
        bool Stop(int instanceId);

        /// <summary>释放事件实例；幂等。</summary>
        void Release(int instanceId);

        /// <summary>暂停或恢复事件。</summary>
        bool SetPaused(int instanceId, bool paused);

        /// <summary>设置事件音量（线性最终值）。</summary>
        bool SetVolume(int instanceId, float volume);

        /// <summary>设置事件音高。</summary>
        bool SetPitch(int instanceId, float pitch);

        /// <summary>设置事件的 3D 世界坐标。</summary>
        bool Set3DAttributes(int instanceId, Vector3 position);

        /// <summary>设置逐次事件参数（数值或标签）。</summary>
        bool SetParameter(int instanceId, AudioParameter parameter);

        /// <summary>设置全局参数（数值或标签）。</summary>
        bool SetGlobalParameter(AudioParameter parameter);

        /// <summary>加载 bank。</summary>
        bool LoadBank(string bank, bool loadSamples);

        /// <summary>卸载 bank。</summary>
        bool UnloadBank(string bank);

        /// <summary>查询 bank 是否已加载。</summary>
        bool IsBankLoaded(string bank);

        /// <summary>设置 bus 音量（原样透传）。</summary>
        bool SetBusVolume(string bus, float volume);

        /// <summary>设置 bus 静音。</summary>
        bool SetBusMuted(string bus, bool muted);

        /// <summary>暂停或恢复 bus。</summary>
        bool SetBusPaused(string bus, bool paused);

        /// <summary>启动 snapshot。</summary>
        bool StartSnapshot(string snapshot);

        /// <summary>停止 snapshot。</summary>
        bool StopSnapshot(string snapshot);

        /// <summary>查询事件播放状态。</summary>
        AudioProviderPlaybackState Poll(int instanceId);
    }
}
