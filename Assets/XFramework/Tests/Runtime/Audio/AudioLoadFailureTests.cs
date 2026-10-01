using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 失败、取消与异常收口。
    /// <para><b>这一组几乎全是失败路径</b>，而失败路径正是本仓缺陷史里最集中的地方（<c>ModuleAudit.md</c>
    /// 的 B3「异常隔离不对称」与 D4「静默失败」）。三条契约在这里钉死：加载失败<b>不抛</b>（返回默认句柄）、
    /// 参数非法<b>抛</b>、取消<b>抛且不泄漏</b>。</para>
    /// <para><b>异步用例写成 <c>async Task</c> 并直接 <c>await</c> UniTask</b>（本仓既有写法）；
    /// 断言异步异常用 <see cref="AudioTestKit.AssertThrowsAsync{T}"/>，不用 <c>Assert.ThrowsAsync</c>。</para>
    /// </summary>
    [TestFixture]
    public class AudioLoadFailureTests
    {
        #region Setup

        private FakeAudioClipLoader _loader;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _loader = new FakeAudioClipLoader();   // Clip 为 null ⇒ 加载失败
            AudioManager.SetInstance(new AudioManagerImpl(new AudioInitOptions { MaxVoices = 4 }, _loader));
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
            UpdateManager.Clear();
        }

        #endregion

        #region 加载失败：不抛，返回默认句柄

        /// <summary>
        /// <see cref="AudioManager.Play"/> 是 fire-and-forget：句柄在<b>预占槽位时</b>就返回，所以调用方拿到
        /// 的是预占句柄。加载失败会让它<b>失效</b>，而不是把它变成 <c>default</c>——「返回 default」是
        /// <see cref="AudioManager.PlayAsync"/> 的契约（它在加载完成之后才返回，故有资格判断成败）。
        /// <para>正因为这条差异，失败不抛这条契约的兑现方式是「句柄可安全探测」，而不是「返回值必然可判空」。</para>
        /// </summary>
        [Test]
        public void Play_WhenLoadFails_ReturnsAHandleThatIsSafelyInvalid()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Audio\] 播放失败，clip 加载未成功"));

            var handle = AudioManager.Play("audio/missing");

            Assert.IsFalse(AudioManager.IsPlaying(handle), "加载失败的句柄必须报告不在播");
            Assert.IsFalse(AudioManager.Stop(handle), "失效句柄上的 Stop 应落空");
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount, "失败的播放不得占住槽位");
        }

        [Test]
        public void Play_WhenLoadFails_DoesNotLeakTheSlot()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Audio\] 播放失败"));

            AudioManager.Play("audio/missing");

            // 槽位已释放：换成能加载成功的 loader 后应能正常播
            var clip = AudioTestKit.CreateClip();
            _loader.Clip = clip;
            try
            {
                var handle = AudioManager.Play("audio/ok", new AudioPlayOptions(loop: true));
                Assert.IsFalse(handle.IsDefault, "失败的那次不该把槽位永久占住");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public async Task PlayAsync_WhenLoadFails_ReturnsDefaultHandle()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Audio\] 播放失败"));

            var handle = await AudioManager.PlayAsync("audio/missing");

            Assert.IsTrue(handle.IsDefault);
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount);
        }

        [Test]
        public async Task PlayAsync_WhenLoadSucceeds_ReturnsHandleThatIsAlreadyPlaying()
        {
            var clip = AudioTestKit.CreateClip();
            _loader.Clip = clip;
            try
            {
                var handle = await AudioManager.PlayAsync("audio/ok", new AudioPlayOptions(loop: true));

                Assert.IsFalse(handle.IsDefault);
                Assert.IsTrue(AudioManager.IsPlaying(handle),
                    "PlayAsync 的句柄返回时必定已起播——这正是它与 Play 的差别");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        #endregion

        #region 加载缝抛异常：兜住，不冒到调用方

        [Test]
        public void Play_WhenLoaderThrows_LogsErrorAndDoesNotLeaveTheSlotBusy()
        {
            _loader.ThrowOnLoad = new InvalidOperationException("boom");

            LogAssert.Expect(LogType.Error, new Regex(@"\[Audio\] 加载音频资源 'audio/x' 时发生未预期异常"));

            // 不抛给调用方即为通过——加载缝的契约是「失败返回无效租约」，真抛了这里也必须兜住
            var handle = AudioManager.Play("audio/x");

            Assert.IsFalse(AudioManager.IsPlaying(handle), "异常路径不得让句柄报告在播");
            Assert.AreEqual(0, AudioManager.ActiveVoiceCount, "异常路径同样不得占住槽位");
        }

        #endregion

        #region 取消：抛 OperationCanceledException，且不泄漏

        [Test]
        public async Task PlayAsync_WhenCancelled_ThrowsAndFreesTheSlot()
        {
            var clip = AudioTestKit.CreateClip();
            _loader.Clip = clip;

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await AudioTestKit.AssertThrowsAsync<OperationCanceledException>(
                    () => AudioManager.PlayAsync("audio/a", AudioPlayOptions.Default, cts.Token),
                    "取消应通过 OperationCanceledException 传播，而不是被吞掉");
            }

            Assert.AreEqual(0, AudioManager.ActiveVoiceCount, "取消必须把预占的槽位还回去");
            Assert.AreEqual(0, _loader.OutstandingLeases, "取消路径不得留下未归还的租约");

            UnityEngine.Object.DestroyImmediate(clip);
        }

        #endregion

        #region Destroy 之后

        [Test]
        public void Destroy_ThenPlay_Throws()
        {
            AudioManager.Destroy();

            Assert.Throws<InvalidOperationException>(() => AudioManager.Play("audio/a"));
        }

        [Test]
        public void Destroy_ThenHandleProbes_AreSafe()
        {
            var clip = AudioTestKit.CreateClip();
            _loader.Clip = clip;
            try
            {
                var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
                Assert.IsTrue(AudioManager.IsPlaying(handle), "前提：确实在播");

                AudioManager.Destroy();

                Assert.IsFalse(AudioManager.IsPlaying(handle), "句柄可以长于管理器，探测必须安全");
                Assert.IsFalse(AudioManager.Stop(handle));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        #endregion
    }
}
