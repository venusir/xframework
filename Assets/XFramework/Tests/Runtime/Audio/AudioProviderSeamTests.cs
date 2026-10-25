using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// <see cref="IAudioProvider"/> 播放引擎缝：核心语义层与自定义 provider 的接线、音量/暂停/回收、
    /// 失败与异常隔离、provider 句柄映射。
    /// </summary>
    [TestFixture]
    public class AudioProviderSeamTests
    {
        #region Setup

        private FakeAudioProvider _fake;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _fake = new FakeAudioProvider();
            AudioManager.SetProvider(_fake, new AudioInitOptions { MaxVoices = 4 });
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
            UpdateManager.Clear();
        }

        #endregion

        #region 注入与请求

        [Test]
        public void SetProvider_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AudioManager.SetProvider(null));
        }

        [Test]
        public void SetProvider_DisposesThePreviousInstance()
        {
            var next = new FakeAudioProvider();

            AudioManager.SetProvider(next);

            Assert.IsTrue(_fake.Disposed, "SetProvider 必须先 Dispose 旧实例（provider 由框架持有）");
            Assert.IsFalse(next.Disposed);
        }

        /// <summary>
        /// 同一实例再传一次会先被 <c>Destroy</c> 提前 Dispose，再装回去就是个已释放的实例。
        /// 这是非法用法，必须显式抛，且抛出前不得动当前实例。
        /// </summary>
        [Test]
        public void SetProvider_SameProvider_ThrowsAndKeepsTheCurrentInstance()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => AudioManager.SetProvider(_fake));

            StringAssert.Contains("[Audio]", ex.Message);
            Assert.IsFalse(_fake.Disposed, "抛异常发生在 Destroy 之前，当前实例必须完好");
            Assert.IsTrue(AudioManager.IsInitialized);
        }

        [Test]
        public void Play_ForwardsLocationAndRequest()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Bgm, loop: true));

            Assert.IsFalse(handle.IsDefault);
            Assert.AreEqual(1, _fake.Plays.Count);
            Assert.AreEqual("audio/a", _fake.LastLocation);
            Assert.IsTrue(_fake.LastRequest.Loop);
            Assert.AreEqual(1, AudioManager.GetActiveVoiceCount(AudioChannels.Bgm));
            Assert.IsTrue(AudioManager.IsPlaying(handle));
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void Play_NormalizesRequestBeforeCallingProvider()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(
                pitch: 0f,
                spatialBlend: 3f,
                position: new Vector3(1f, 2f, 3f)));

            var request = _fake.LastRequest;

            Assert.AreEqual(1f, request.Pitch, 0.001f, "非正音高必须归一化为 1");
            Assert.AreEqual(1f, request.SpatialBlend, 0.001f, "空间混合必须钳到 [0,1]");
            Assert.AreEqual(new Vector3(1f, 2f, 3f), request.Position);
            Assert.IsTrue(request.ValidityToken.CanBeCanceled);
            Assert.IsFalse(request.ValidityToken.IsCancellationRequested);
        }

        [Test]
        public void Volume_IsComputedByCoreAndPushedToProvider()
        {
            AudioManager.MasterVolume = 0.5f;
            AudioManager.SetChannelVolume(AudioChannels.Bgm, 0.5f);

            AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Bgm, 0.5f, loop: true));

            Assert.AreEqual(0.125f, _fake.LastRequest.Volume, 0.001f, "主 × 通道 × 单次 = 0.125");
            Assert.GreaterOrEqual(_fake.SetVolumeCalls, 1);
            Assert.AreEqual(0.125f, _fake.Find(FindProviderHandle()).Volume, 0.001f);
        }

        [Test]
        public void MasterVolumeChange_PushesToProvider()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.MasterVolume = 0.25f;

            Assert.AreEqual(0.25f, _fake.Find(FindProviderHandle()).Volume, 0.001f);
            Assert.GreaterOrEqual(_fake.SetVolumeCalls, 2);
        }

        [Test]
        public void PauseAndResume_ForwardToProvider()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.Pause();
            Assert.IsTrue(AudioManager.IsPlaying(handle), "暂停中的播放仍算在播");
            Assert.IsTrue(_fake.Find(FindProviderHandle()).Paused);

            AudioManager.Resume();
            Assert.IsFalse(_fake.Find(FindProviderHandle()).Paused);
            Assert.IsTrue(AudioManager.IsPlaying(handle));
        }

        #endregion

        #region 回收、停止与失败

        [Test]
        public void NaturalEnd_IsReapedBySweep()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.End(FindProviderHandle());

            ((AudioManagerImpl)AudioManager.CurrentManagerForTests).SweepVoices();

            Assert.IsFalse(AudioManager.IsPlaying(handle));
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.AreEqual(1, _fake.ReleaseCalls);
        }

        [Test]
        public void Stop_ReleasesProviderHandle()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsTrue(AudioManager.Stop(handle));

            Assert.AreEqual(1, _fake.StopCalls);
            Assert.AreEqual(1, _fake.ReleaseCalls);
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.IsFalse(AudioManager.TryGetProviderHandle(handle, out _));
        }

        [Test]
        public void ProviderFailure_PlayReturnsInvalidPreReservedHandle_PlayAsyncReturnsDefault()
        {
            _fake.FailNext = true;
            var handle = AudioManager.Play("audio/a");

            Assert.IsFalse(handle.IsDefault, "Play 的契约是预占即返回");
            Assert.IsFalse(AudioManager.IsPlaying(handle));
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public async Task ProviderFailure_PlayAsyncReturnsDefault()
        {
            _fake.FailNext = true;

            var handle = await AudioManager.PlayAsync("audio/a");

            Assert.IsTrue(handle.IsDefault);
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        #endregion

        #region 句柄映射与诊断

        [Test]
        public void TryGetProviderHandle_Probes()
        {
            Assert.IsFalse(AudioManager.TryGetProviderHandle(default, out _), "default 句柄必须返回 false");
            Assert.IsFalse(AudioManager.TryGetProviderHandle(new AudioHandle(99, 1), out _),
                "未知句柄必须返回 false");

            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsTrue(AudioManager.TryGetProviderHandle(handle, out AudioProviderHandle providerHandle));
            Assert.IsFalse(providerHandle.IsDefault);
            Assert.IsNotNull(_fake.Find(providerHandle));

            AudioManager.Destroy();
            Assert.IsFalse(AudioManager.TryGetProviderHandle(handle, out _),
                "已销毁的管理器必须安全返回 false");
        }

        [Test]
        public void CopyChannels_WorksWithCustomProvider()
        {
            AudioManager.SetChannelVolume(AudioChannels.Bgm, 0.4f);
            AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Bgm, loop: true));

            var buffer = new List<AudioChannelInfo>();
            int count = AudioManager.CopyChannels(buffer);

            Assert.AreEqual(1, count);
            Assert.AreEqual(AudioChannels.Bgm, buffer[0].Channel);
            Assert.AreEqual(0.4f, buffer[0].Volume, 0.001f);
            Assert.AreEqual(1, buffer[0].ActiveVoiceCount);
        }

        [Test]
        public void ProviderHandle_EqualityAndDefault()
        {
            Assert.IsTrue(default(AudioProviderHandle).IsDefault);
            Assert.IsTrue(new AudioProviderHandle(0, 0).IsDefault);

            var a = new AudioProviderHandle(1, 1);
            var b = new AudioProviderHandle(1, 1);
            var c = new AudioProviderHandle(1, 2);

            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.IsTrue(a != c);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        #endregion

        #region provider 抛异常：核心隔离

        [Test]
        public void ProviderThrowsOnPlay_IsIsolated()
        {
            _fake.ThrowOnPlay = true;
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 播放 'audio/a' 时发生未预期异常")));

            var handle = AudioManager.Play("audio/a");

            Assert.IsFalse(handle.IsDefault, "Play 的预占句柄仍然返回");
            Assert.IsFalse(AudioManager.IsPlaying(handle));
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ProviderThrowsOnPoll_IsIsolatedAndReclaims()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.ThrowOnPoll = true;
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 轮询音频播放状态时发生异常")));

            ((AudioManagerImpl)AudioManager.CurrentManagerForTests).SweepVoices();

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ProviderThrowsOnVolume_IsIsolatedAndReclaims()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.ThrowOnSetVolume = true;
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 刷新音频音量时发生异常")));

            AudioManager.MasterVolume = 0.5f;

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ProviderThrowsOnPause_IsIsolatedAndReclaims()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.ThrowOnSetPaused = true;
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 暂停音频播放时发生异常")));

            AudioManager.Pause();

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ProviderStopAndReleaseThrow_AreIsolatedAndReclaimed()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.ThrowOnStop = true;
            _fake.ThrowOnRelease = true;

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 停止音频播放时发生异常")));
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 释放音频播放时发生异常")));

            Assert.IsTrue(AudioManager.Stop(handle), "provider 抛异常也不能让 Stop 抛出去");
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void ProviderDisposeThrow_IsIsolatedByFacade()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.ThrowOnDispose = true;

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] Destroy 时 Dispose 管理器实例抛出异常")));

            Assert.DoesNotThrow(() => AudioManager.Destroy());
            Assert.IsFalse(AudioManager.IsInitialized, "异常不得让门面卡在半初始化态");
        }

        /// <summary>
        /// provider 在 validity 令牌上注册的取消回调抛异常时，<c>Stop</c> 仍须完成槽位回收；
        /// 否则一次回调异常会把整个槽位表卡在 Loading（C1）。
        /// </summary>
        [Test]
        public async Task CancelCallbackThrows_StopIsIsolated()
        {
            _fake.DelayNext = true;
            _fake.ThrowOnValidityCancel = true;

            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            Assert.IsFalse(handle.IsDefault, "前提：已预占核心槽位");

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 停止加载中的音频时取消有效性令牌发生异常")));

            Assert.IsTrue(AudioManager.Stop(handle), "取消回调抛异常后仍须回收槽位并返回 true");
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);

            _fake.CompletePendingPlay();
            await UniTask.DelayFrame(1);

            Assert.AreEqual(0, _fake.LiveSlotCount);
        }

        /// <summary>
        /// <c>Destroy</c> 路径同理：取消回调抛异常也不能跳过 <c>provider.Dispose</c>（C1）。
        /// </summary>
        [Test]
        public async Task CancelCallbackThrows_DestroyIsIsolated()
        {
            _fake.DelayNext = true;
            _fake.ThrowOnValidityCancel = true;

            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[Audio] 销毁音频管理器时取消槽位有效性令牌发生异常")));

            Assert.DoesNotThrow(() => AudioManager.Destroy());
            Assert.IsTrue(_fake.Disposed, "取消回调抛异常后 provider.Dispose 仍必须执行");

            _fake.CompletePendingPlay();
            await UniTask.DelayFrame(1);
        }

        #endregion

        #region 状态机兜底（C2/C3）

        /// <summary>provider 一直报 Starting：60 拍后必须强制回收并告警，不能永久占槽。</summary>
        [Test]
        public void StartingTimeout_After60Polls_ReleasesAndLogs()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            var impl = (AudioManagerImpl)AudioManager.CurrentManagerForTests;

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
                "[Audio] 播放源起播超时")));

            for (int i = 0; i <= AudioManagerImpl.StartingTimeoutTicks; i++)
                impl.SweepVoices();

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.GreaterOrEqual(_fake.PollCalls, AudioManagerImpl.StartingTimeoutTicks + 1);
            Assert.AreEqual(1, _fake.ReleaseCalls, "超时回收必须把 provider 实例也释放掉");
        }

        /// <summary>未知 Poll 状态必须兜底回收：漏掉它会让唯一 voice 既不算活跃也不被回收（C3）。</summary>
        [Test]
        public void UnknownPollState_IsReclaimedAndWarns()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.PollOverride = (AudioProviderPlaybackState)99;

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
                "[Audio] provider 返回了未知的播放状态（99）")));

            ((AudioManagerImpl)AudioManager.CurrentManagerForTests).SweepVoices();

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.AreEqual(1, _fake.ReleaseCalls);
        }

        /// <summary>核心处于 Playing、provider 却回退报 Starting：必须重新累计超时并最终回收（C3）。</summary>
        [Test]
        public void PlayingThenStarting_TimesOutAndReleases()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            var impl = (AudioManagerImpl)AudioManager.CurrentManagerForTests;
            var slot = _fake.Find(_fake.LastSuccessHandle);

            slot.State = AudioProviderPlaybackState.Playing;
            impl.SweepVoices();
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "前提：核心已观察到 Playing");

            slot.State = AudioProviderPlaybackState.Starting;

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(
                "[Audio] 播放源起播超时")));

            for (int i = 0; i <= AudioManagerImpl.StartingTimeoutTicks; i++)
                impl.SweepVoices();

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        /// <summary>管理器暂停期间也要 poll：外部销毁的播放源不会自己变回来（C2）。</summary>
        [Test]
        public void PausedVoice_ProviderReportsInvalid_IsReclaimed()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Pause();
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "前提：暂停中的播放仍占槽");

            _fake.End(_fake.LastSuccessHandle);

            ((AudioManagerImpl)AudioManager.CurrentManagerForTests).SweepVoices();

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount, "暂停期间的死源必须被回收");
            Assert.AreEqual(1, _fake.ReleaseCalls);
        }

        [Test]
        public void SetPausedFalse_Reclaims()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.RejectSetPaused = true;

            AudioManager.Pause();

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.AreEqual(1, _fake.ReleaseCalls);
        }

        [Test]
        public void SetVolumeFalse_Reclaims()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            _fake.RejectSetVolume = true;

            AudioManager.MasterVolume = 0.5f;

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.AreEqual(1, _fake.ReleaseCalls);
        }

        #endregion

        #region 加载窗口（provider 延迟返回）

        [Test]
        public async Task StopAllWhilePlayAsyncLoading_ReturnsDefault()
        {
            _fake.DelayNext = true;
            var task = AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));
            Assert.IsTrue(_fake.HasPendingPlay);
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "加载中的播放也算活跃");

            Assert.AreEqual(1, AudioManager.StopAll());

            _fake.CompletePendingPlay();
            var handle = await task;

            Assert.IsTrue(handle.IsDefault, "加载中 StopAll 后 PlayAsync 应返回 default，而不是抛 OCE");
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.AreEqual(0, _fake.LiveSlotCount);
        }

        [Test]
        public async Task PauseAndVolume_WhileProviderLoading_AppliedAfterStart()
        {
            _fake.DelayNext = true;
            var task = AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.Pause();
            AudioManager.MasterVolume = 0.5f;

            _fake.CompletePendingPlay();
            var handle = await task;

            Assert.IsFalse(handle.IsDefault);
            Assert.IsTrue(AudioManager.IsPaused);
            Assert.IsTrue(AudioManager.IsPlaying(handle), "暂停中的播放仍算在播");

            var slot = _fake.Find(_fake.LastSuccessHandle);
            Assert.IsNotNull(slot);
            Assert.IsTrue(slot.Paused, "加载窗口内发生的管理器暂停必须在起播后补上");
            Assert.AreEqual(0.5f, slot.Volume, 0.001f, "加载窗口内发生的音量变更必须在起播后生效");
        }

        [Test]
        public async Task CallerCancelledWhileProviderLoading_ThrowsAndReleases()
        {
            _fake.DelayNext = true;
            var cts = new CancellationTokenSource();
            var task = AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true), cts.Token);

            cts.Cancel();
            _fake.CompletePendingPlay();

            await AudioTestKit.AssertThrowsAsync<OperationCanceledException>(async () => await task,
                "调用方在加载窗口内取消时 PlayAsync 必须抛 OCE");

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.AreEqual(0, _fake.LiveSlotCount);
        }

        /// <summary>
        /// provider 忽略调用方令牌并返回句柄：核心必须在返回后补检查，收掉已起播的实例并抛 OCE（C5）。
        /// </summary>
        [Test]
        public async Task CallerCancelled_ProviderIgnoresToken_StillThrowsAndReleases()
        {
            _fake.DelayNext = true;
            _fake.IgnoreCancellation = true;
            var cts = new CancellationTokenSource();
            var task = AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true), cts.Token);

            cts.Cancel();
            _fake.CompletePendingPlay();

            await AudioTestKit.AssertThrowsAsync<OperationCanceledException>(async () => await task,
                "provider 忽略令牌不代表 PlayAsync 可以不抛");

            Assert.AreEqual(1, _fake.StopCalls, "provider 已交出的实例必须被收掉");
            Assert.AreEqual(1, _fake.ReleaseCalls);
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        /// <summary>provider 忽略令牌且返回 default：核心仍须抛 OCE，而不是静默返回 default（C5）。</summary>
        [Test]
        public async Task CallerCancelled_ProviderIgnoresTokenReturnsDefault_StillThrows()
        {
            _fake.DelayNext = true;
            _fake.IgnoreCancellation = true;
            _fake.FailNext = true;
            var cts = new CancellationTokenSource();
            var task = AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true), cts.Token);

            cts.Cancel();
            _fake.CompletePendingPlay();

            await AudioTestKit.AssertThrowsAsync<OperationCanceledException>(async () => await task,
                "返回 default 也不能把已取消的 PlayAsync 变成静默成功");

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
            Assert.AreEqual(0, _fake.StopCalls);
        }

        #endregion

        #region 容量与整体流程

        [Test]
        public void MaxVoices_WithCustomProvider_ReturnsDefaultAndWarns()
        {
            for (int i = 0; i < 4; i++)
            {
                Assert.IsFalse(AudioManager.Play($"audio/{i}", new AudioPlayOptions(loop: true)).IsDefault,
                    $"前提：第 {i + 1} 个播放应预占到槽位");
            }

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("[Audio] 播放源已耗尽")));

            Assert.IsTrue(AudioManager.Play("audio/x", new AudioPlayOptions(loop: true)).IsDefault);
            Assert.AreEqual(4, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public void StopAll_WithCustomProvider_StopsAll()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));

            Assert.AreEqual(2, AudioManager.StopAll());

            Assert.AreEqual(2, _fake.StopCalls);
            Assert.AreEqual(2, _fake.ReleaseCalls);
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public async Task PlayAsync_WhenProviderSucceeds_ReturnsHandle()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsFalse(handle.IsDefault);
            Assert.IsTrue(AudioManager.IsPlaying(handle));
            Assert.IsTrue(AudioManager.TryGetProviderHandle(handle, out AudioProviderHandle providerHandle));
            Assert.IsNotNull(_fake.Find(providerHandle));
        }

        /// <summary>
        /// 正常结束的槽位复用时复用同一条未取消的有效性令牌源——每次 <c>Play</c> 新建一条 CTS
        /// 是纯分配，同一槽位连续播放时尤其明显。
        /// </summary>
        [Test]
        public void ReusedSlot_ReusesValidityToken_WhenNotCancelled()
        {
            var first = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Stop(first);
            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));

            Assert.AreEqual(2, _fake.Plays.Count);
            var firstToken = _fake.Plays[0].Request.ValidityToken;
            var secondToken = _fake.Plays[1].Request.ValidityToken;

            Assert.IsFalse(firstToken.IsCancellationRequested, "前提：正常结束不作废有效性令牌");
            Assert.AreEqual(firstToken, secondToken, "复用槽位时应复用未取消的有效性令牌源");
        }

        /// <summary>
        /// 加载中被 <c>Stop</c> 的令牌已经取消，不能被下一次预占复用——否则新播放一开始就是作废的。
        /// </summary>
        [Test]
        public async Task CancelledValidityToken_IsReplacedOnNextReservation()
        {
            _fake.DelayNext = true;
            var first = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            var cancelledToken = _fake.LastRequest.ValidityToken;

            AudioManager.Stop(first);
            Assert.IsTrue(cancelledToken.IsCancellationRequested, "前提：加载中被 Stop 的令牌已取消");

            _fake.CompletePendingPlay();
            await UniTask.DelayFrame(1);

            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));
            var nextToken = _fake.LastRequest.ValidityToken;

            Assert.AreNotEqual(cancelledToken, nextToken, "已取消的令牌不得复用");
            Assert.IsFalse(nextToken.IsCancellationRequested);
        }

        /// <summary>
        /// 自定义 provider 的容量小于核心 <c>MaxVoices</c>：核心按播放失败回收自己的槽位，
        /// 不留下「句柄还在、声音没有」的僵尸（C4）。
        /// </summary>
        [Test]
        public async Task CustomProviderSmallerCapacity_PlayFailsSafely()
        {
            _fake.MaxConcurrent = 1;

            var first = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            Assert.IsFalse(first.IsDefault);
            Assert.IsTrue(AudioManager.IsPlaying(first));

            var second = AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));
            Assert.IsFalse(second.IsDefault, "Play 的契约是预占即返回，失败发生在返回之后");
            Assert.IsFalse(AudioManager.IsPlaying(second));
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "核心槽位必须随 provider 失败回收");

            var third = await AudioManager.PlayAsync("audio/c", new AudioPlayOptions(loop: true));
            Assert.IsTrue(third.IsDefault);
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount);
        }

        #endregion

        #region Helpers

        /// <summary>最近一次成功播放的 provider 句柄（由替身记录，失败请求不会污染它）。</summary>
        private AudioProviderHandle FindProviderHandle() => _fake.LastSuccessHandle;

        #endregion
    }
}
