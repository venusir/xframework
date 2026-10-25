using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XLog;

namespace XFramework.XAudio
{
    /// <summary>
    /// 内置播放引擎的宿主与槽位池。
    /// <para><b>它只服务 <see cref="AudioSourceProvider"/></b>：容量由核心的槽位表决定，这里只按同一
    /// <c>MaxVoices</c> 建 GameObject；池满返回 false，核心按播放失败处理。</para>
    /// <para><b>回收判据（<c>AudioListener.pause</c>、死源）在 provider 的 <c>Poll</c> 里</b>；
    /// <c>Starting</c> 超时由核心 <see cref="AudioManagerImpl"/> 的 ticker 负责。池本身不再扫尾。</para>
    /// </summary>
    internal sealed class AudioSourcePool
    {
        #region Private Fields

        private readonly List<AudioVoice> _slots = new List<AudioVoice>();
        private readonly int _maxVoices;

        private AudioHost _host;

        /// <summary>本轮「播放源耗尽」是否已告警过。任一次释放即复位，使下一次耗尽能再提醒一次。</summary>
        private bool _exhaustionWarned;

        #endregion

        #region Constructors

        internal AudioSourcePool(int maxVoices)
        {
            _maxVoices = maxVoices < 1 ? 1 : maxVoices;
        }

        #endregion

        #region Internal

        /// <summary>已创建的槽位数（含空闲的）。</summary>
        internal int SlotCount => _slots.Count;

        internal AudioVoice GetSlot(int index) => _slots[index];

        /// <summary>
        /// 一次性建满宿主与全部槽位（<see cref="AudioInitOptions.PrewarmVoices"/>）。
        /// </summary>
        internal void Prewarm()
        {
            EnsureHost();

            while (_slots.Count < _maxVoices)
                _slots.Add(CreateVoice(_slots.Count));
        }

        /// <summary>
        /// 预占一个 provider 槽位。失败即「播放源已耗尽」，调用方退化成返回默认句柄。
        /// <para>预占发生在<b>发起加载之前</b>：池满时一次 IO 都不会浪费。</para>
        /// </summary>
        /// <param name="location">资源地址，仅作诊断记录。</param>
        /// <param name="index">预占到的槽位下标。</param>
        /// <param name="handle">本次播放的 provider 句柄。</param>
        internal bool TryReserve(string location, out int index, out AudioProviderHandle handle)
        {
            index = -1;
            handle = default;

            EnsureHost();

            int free = -1;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].State == AudioVoiceState.Free)
                {
                    free = i;
                    break;
                }
            }

            if (free < 0)
            {
                if (_slots.Count >= _maxVoices)
                {
                    WarnExhaustedOnce();
                    return false;
                }

                free = _slots.Count;
                _slots.Add(CreateVoice(free));
            }

            var voice = _slots[free];

            // 外部销毁宿主/播放源后，Free 槽可能留着 Unity 伪 null 的 Source；不重建就会让
            // 每一次播放都命中同一个坏槽并失败，健康槽位永远轮不到（R4-AUD-001）。
            if (voice.Source == null)
            {
                voice = RebuildVoice(voice, free);
                _slots[free] = voice;
            }

            voice.Generation = NextGeneration(voice.Generation);
            voice.State = AudioVoiceState.Loading;
            voice.Location = location;
            voice.Loop = false;
            voice.ClipLease = default;

            index = free;
            handle = new AudioProviderHandle(free, voice.Generation);
            return true;
        }

        /// <summary>
        /// 该 (槽位, 代际) 是否仍是当初预占的那一代且未被释放。
        /// <para>加载续体靠它发现「期间被 Stop / StopAll / Destroy 作废了」。</para>
        /// </summary>
        internal bool IsCurrent(int index, int generation)
        {
            if (generation == 0 || index < 0 || index >= _slots.Count)
                return false;

            var voice = _slots[index];
            return voice.Generation == generation && voice.State != AudioVoiceState.Free;
        }

        /// <summary>
        /// 释放一个槽位：停播、清 clip、释放资源句柄、置空闲。
        /// <para>代际<b>不在这里</b>递增——置空闲已足够让同代句柄失效（<see cref="IsCurrent"/> 会因
        /// <see cref="AudioVoiceState.Free"/> 返回 false）；下一次预占时才递增。</para>
        /// </summary>
        internal void Release(int index)
        {
            if (index < 0 || index >= _slots.Count)
                return;

            var voice = _slots[index];

            if (voice.Source != null)
            {
                voice.Source.Stop();
                voice.Source.clip = null;
            }

            DisposeClipLease(voice);

            voice.State = AudioVoiceState.Free;
            voice.Location = null;
            voice.Loop = false;
            voice.ClipLease = default;
            _exhaustionWarned = false;
        }

        /// <summary>
        /// 作废一个仍在加载中的槽位（核心槽位被 <c>Stop</c>/<c>StopAll</c> 取消）。
        /// <para><b>立即归还池槽</b>：否则 <c>Stop</c> 之后紧接着的重播会撞上「池满」窗口，拿到一个
        /// 必然失效的预占句柄（P1-1）。</para>
        /// <para><b>不动租约</b>：此时租约还在 provider <c>PlayAsync</c> 的局部变量里（槽位只在起播
        /// 全部成功后才附着租约），由该调用的 <c>ReleaseReservation</c> 释放——释放点必须恰好一次。</para>
        /// <para><b>代际递增</b>：晚到的加载续体靠它发现「槽位已不是自己那一代」，从而不再碰池槽。</para>
        /// </summary>
        internal void Invalidate(int index, int generation)
        {
            if (!IsCurrent(index, generation))
                return;

            Release(index);
            _slots[index].Generation = NextGeneration(_slots[index].Generation);
        }

        /// <summary>释放全部槽位与宿主。</summary>
        internal void Dispose()
        {
            for (int i = 0; i < _slots.Count; i++)
                Release(i);

            _slots.Clear();
            _exhaustionWarned = false;

            if (_host != null)
            {
                UnityEngine.Object.Destroy(_host.gameObject);
                _host = null;
            }
        }

        #endregion

        #region Private

        /// <summary>代际递增并跳过 <c>0</c>——<c>0</c> 是 <c>default(AudioProviderHandle)</c> 的保留值。</summary>
        private static int NextGeneration(int current)
        {
            int next = current + 1;
            return next == 0 ? 1 : next;
        }

        /// <summary>首次预占时惰性创建宿主。从不播放的项目一个 GameObject 都不会建。</summary>
        private void EnsureHost()
        {
            if (_host != null)
                return;

            var go = new GameObject("[AudioManager] Audio Host");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _host = go.AddComponent<AudioHost>();
        }

        private AudioVoice CreateVoice(int index)
        {
            var go = new GameObject($"Voice {index}");
            go.transform.SetParent(_host.transform, false);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;

            return new AudioVoice { Source = source, SlotObject = go };
        }

        /// <summary>
        /// 在原下标重建坏掉的槽位（R4-AUD-001）。
        /// <para>两种外部销毁：只销毁 <see cref="AudioSource"/> 组件时复用槽位对象补组件；
        /// 宿主整体销毁时旧槽位对象已不在，按原名新建并保留旧代际（随后由 <see cref="TryReserve"/> 递增）。</para>
        /// </summary>
        private AudioVoice RebuildVoice(AudioVoice voice, int index)
        {
            EnsureHost();

            if (voice.SlotObject != null)
            {
                var source = voice.SlotObject.GetComponent<AudioSource>();
                if (source == null)
                    source = voice.SlotObject.AddComponent<AudioSource>();

                source.playOnAwake = false;
                voice.Source = source;
                return voice;
            }

            var rebuilt = CreateVoice(index);
            rebuilt.Generation = voice.Generation;
            return rebuilt;
        }

        /// <summary>池耗尽告警：同一段耗尽期只记一条，免得一帧几十个音效把控制台刷爆。</summary>
        private void WarnExhaustedOnce()
        {
            if (_exhaustionWarned)
                return;

            _exhaustionWarned = true;
            LogManager.Warning(LogCategories.Audio,
                "播放源已耗尽（MaxVoices={0}），本次播放被丢弃。" +
                "调大 AudioInitOptions.MaxVoices，或缩短同时播放的音效数量。",
                _maxVoices);
        }

        /// <summary>
        /// 释放资源句柄。
        /// <para>用 try/catch 包住：<c>Dispose</c> 可能由 <c>Application.quitting</c> 触发，而处理器顺序
        /// 不保证 <c>XAsset</c> 的包还没销毁——那时释放会抛。退出路径上不该再炸一次。</para>
        /// </summary>
        private static void DisposeClipLease(AudioVoice voice)
        {
            // 不能按 IsValid 早退（AUD-004）：没有资源本体不代表没有释放动作。
            try
            {
                voice.ClipLease.Dispose();
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Audio, e,
                    "释放音频资源租约时发生异常（槽位资源）");
            }
        }

        #endregion
    }
}
