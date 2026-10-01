using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// Audio 侧用例的共用小工具。
    /// </summary>
    internal static class AudioTestKit
    {
        /// <summary>
        /// 造一个运行时 <see cref="AudioClip"/>，不依赖任何资源管线。
        /// <para><b>实测已确认</b>（见 <c>Documentation/Modules/Audio.md</c> 的实测记录）：batchmode
        /// （无音频设备）下这种 clip 能正常播放，<c>isPlaying</c> 与 <c>time</c> 都会推进——因此
        /// 回收相关的用例可以走真实播放驱动，不需要造内部 seam。</para>
        /// </summary>
        /// <param name="name">clip 名。</param>
        /// <param name="seconds">时长（秒）。默认 0.1 秒，用例里等它播完很快。</param>
        internal static AudioClip CreateClip(string name = "test-clip", float seconds = 0.1f)
        {
            const int sampleRate = 44100;
            int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            return AudioClip.Create(name, samples, 1, sampleRate, false);
        }

        /// <summary>
        /// 取某个句柄对应的播放源，用来直接断言写到 <c>AudioSource.volume</c> 上的实际值。
        /// <para><b>刻意不经引擎的音量语义</b>（<c>AudioListener.volume</c>、Mixer 总线等）：框架的承诺就是
        /// 「写进播放源的值是多少」，直接读它才能把承诺钉住。</para>
        /// <para>槽位 GameObject 的命名（<c>"Voice {id}"</c>）是内部实现细节，本工具是唯一依赖它的地方；
        /// 查找时优先取正在播放的那个，避免撞上仍在等待延迟销毁的旧宿主。</para>
        /// </summary>
        internal static AudioSource FindSourceFor(AudioHandle handle)
        {
            var hosts = UnityEngine.Object.FindObjectsByType<AudioHost>(FindObjectsInactive.Include);
            AudioSource fallback = null;

            for (int i = 0; i < hosts.Length; i++)
            {
                var child = hosts[i].transform.Find($"Voice {handle.Id}");
                if (child == null)
                    continue;

                var source = child.GetComponent<AudioSource>();
                if (source == null)
                    continue;

                if (source.isPlaying)
                    return source;

                if (fallback == null)
                    fallback = source;
            }

            return fallback;
        }

        /// <summary>直接读句柄对应播放源的当前音量；找不到播放源时断言失败。</summary>
        internal static float VolumeOf(AudioHandle handle)
        {
            var source = FindSourceFor(handle);
            Assert.IsNotNull(source, "找不到句柄对应的播放源——宿主或槽位没建起来？");
            return source.volume;
        }

        /// <summary>
        /// 断言异步操作抛出指定类型的异常。
        /// <para><b>为什么不直接用 <c>Assert.ThrowsAsync</c></b>：那条通道会阻塞主线程等任务完成，
        /// 与 UniTask 的主线程恢复语义冲突（本仓已在 <c>SaveManagerImplTests</c> 里记下这条并改用
        /// 显式的 try/catch）。</para>
        /// </summary>
        /// <typeparam name="T">期望的异常类型。</typeparam>
        /// <param name="action">被断言的异步操作。</param>
        /// <param name="message">断言失败时的说明。</param>
        internal static async Task AssertThrowsAsync<T>(Func<UniTask> action, string message = null)
            where T : Exception
        {
            try
            {
                await action();
            }
            catch (T)
            {
                return;
            }

            Assert.Fail(message ?? $"Expected {typeof(T).Name} to be thrown.");
        }
    }
}
