using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XAudio
{
    /// <summary>
    /// 播放源池：管理隐藏宿主、槽位分配与回收、以及播完后的扫尾判定。
    /// <para><b>不依赖 <c>PoolManager</c></b>：那套池只服务纯 C# 对象，不涉及 GameObject 与资源引用
    /// （预设体的池在 <c>XAsset</c> 侧）。本池是第三套，自持。</para>
    /// <para><b>唯一并发策略是容量上限</b>：取不到空闲槽位时返回 <c>false</c>，由调用方退化成
    /// 「返回默认句柄 + 记一条限流告警」。框架不做优先级抢占。</para>
    /// </summary>
    internal sealed class AudioSourcePool
    {
        #region Constants

        /// <summary>
        /// <see cref="AudioVoiceState.Starting"/> 的扫描次数上限。
        /// <para>按 <c>Tier1</c> 约 33ms 一拍，60 拍约 2 秒。兜底的是「某个平台上 <c>Play()</c> 之后
        /// <c>isPlaying</c> 永不变真」这种情形——没有它，那个槽位会永久泄漏。</para>
        /// </summary>
        private const int StartingTimeoutTicks = 60;

        #endregion

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

        /// <summary>容量上限。</summary>
        internal int MaxVoices => _maxVoices;

        internal AudioVoice GetSlot(int index) => _slots[index];

        /// <summary>当前活跃槽位数（非 <see cref="AudioVoiceState.Free"/>，含加载中）。</summary>
        internal int CountActive()
        {
            int active = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].State != AudioVoiceState.Free)
                    active++;
            }

            return active;
        }

        /// <summary>宿主是否已创建（测试用来断言「从不播放就不建对象」）。</summary>
        internal bool HasHost => _host != null;

        /// <summary>
        /// 预占一个槽位。失败即「播放源已耗尽」，调用方应退化成返回默认句柄。
        /// <para>预占发生在<b>发起加载之前</b>：池满时一次 IO 都不会浪费。</para>
        /// </summary>
        /// <param name="channel">已归一化并校验过的通道名。</param>
        /// <param name="location">资源地址，仅作诊断记录。</param>
        /// <param name="index">预占到的槽位下标。</param>
        /// <param name="handle">本次播放的句柄。</param>
        internal bool TryReserve(string channel, string location, out int index, out AudioHandle handle)
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
            voice.Generation = NextGeneration(voice.Generation);
            voice.State = AudioVoiceState.Loading;
            voice.Channel = channel;
            voice.Location = location;
            voice.Loop = false;
            voice.VolumeScale = 1f;
            voice.StartingTicks = 0;
            voice.ClipLease = default;

            index = free;
            handle = new AudioHandle(free, voice.Generation);
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
        /// <see cref="AudioVoiceState.Free"/> 返回 false）；下一次预占时才递增，从而挡住跨代的旧句柄。</para>
        /// </summary>
        internal void Release(int index)
        {
            var voice = _slots[index];

            if (voice.Source != null)
            {
                voice.Source.Stop();
                voice.Source.clip = null;
            }

            DisposeClipLease(voice);

            voice.State = AudioVoiceState.Free;
            voice.Channel = null;
            voice.Location = null;
            voice.Loop = false;
            voice.VolumeScale = 1f;
            voice.StartingTicks = 0;

            _exhaustionWarned = false;
        }

        /// <summary>
        /// 作废一个仍在加载中的槽位。
        /// <para><b>不动 <see cref="AudioVoice.ClipLease"/></b>：此时它还是 <c>default</c>；若加载已经完成、
        /// 租约正握在续体手里，那由续体的竞态检查负责释放——释放点必须恰好一次。</para>
        /// </summary>
        internal void Invalidate(int index)
        {
            var voice = _slots[index];
            voice.Generation = NextGeneration(voice.Generation);
            voice.State = AudioVoiceState.Free;
            voice.Channel = null;
            voice.Location = null;
            voice.Loop = false;
            voice.VolumeScale = 1f;
            voice.StartingTicks = 0;

            _exhaustionWarned = false;
        }

        /// <summary>
        /// 扫一遍槽位，回收播完的。返回是否仍有活跃槽位（false 时调用方应注销 ticker）。
        /// <para><b>判据是自持状态机而非只看 <c>isPlaying</c></b>：实测表明 <c>Pause()</c> 之后
        /// <c>isPlaying</c> 会变 <c>false</c>，只看它会把暂停中的播放当场回收。</para>
        /// </summary>
        internal bool Sweep()
        {
            int active = 0;

            for (int i = 0; i < _slots.Count; i++)
            {
                var voice = _slots[i];

                switch (voice.State)
                {
                    case AudioVoiceState.Free:
                        break;

                    case AudioVoiceState.Loading:
                        // 加载中绝不动它——续体负责收尾
                        active++;
                        break;

                    case AudioVoiceState.Starting:
                        if (voice.Source.isPlaying)
                        {
                            voice.State = AudioVoiceState.Playing;
                            active++;
                        }
                        else if (++voice.StartingTicks > StartingTimeoutTicks)
                        {
                            Debug.LogWarning($"[Audio] 播放源起播超时（'{voice.Location}'），槽位被强制回收。" +
                                             "这通常意味着该平台的 AudioSource 在 Play() 后不会报告 isPlaying。");
                            Release(i);
                        }
                        else
                        {
                            active++;
                        }

                        break;

                    case AudioVoiceState.Paused:
                        // 暂停中的播放绝不被回收：Pause() 之后 isPlaying 就是 false，只看它会把暂停中的
                        // 播放当场回收掉（这正是本状态存在的理由）
                        active++;
                        break;

                    case AudioVoiceState.Playing:
                        // 循环的播放永不被自动回收——必须显式 Stop
                        if (voice.Loop || voice.Source.isPlaying)
                            active++;
                        else
                            Release(i);

                        break;
                }
            }

            return active > 0;
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

        /// <summary>代际递增并跳过 <c>0</c>——<c>0</c> 是 <c>default(AudioHandle)</c> 的保留值。</summary>
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

            return new AudioVoice { Source = source };
        }

        /// <summary>池耗尽告警：同一段耗尽期只记一条，免得一帧几十个音效把控制台刷爆。</summary>
        private void WarnExhaustedOnce()
        {
            if (_exhaustionWarned)
                return;

            _exhaustionWarned = true;
            Debug.LogWarning($"[Audio] 播放源已耗尽（MaxVoices={_maxVoices}），本次播放被丢弃。" +
                             "调大 AudioInitOptions.MaxVoices，或缩短同时播放的音效数量。");
        }

        /// <summary>
        /// 释放资源句柄。
        /// <para>用 try/catch 包住：<c>Dispose</c> 可能由 <c>Application.quitting</c> 触发，而处理器顺序
        /// 不保证 <c>XAsset</c> 的包还没销毁——那时释放会抛。退出路径上不该再炸一次。</para>
        /// </summary>
        private static void DisposeClipLease(AudioVoice voice)
        {
            if (!voice.ClipLease.IsValid)
            {
                voice.ClipLease = default;
                return;
            }

            try
            {
                voice.ClipLease.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Audio] 释放音频资源租约时发生异常（'{voice.Location}'）：{e}");
            }

            voice.ClipLease = default;
        }

        #endregion
    }
}
