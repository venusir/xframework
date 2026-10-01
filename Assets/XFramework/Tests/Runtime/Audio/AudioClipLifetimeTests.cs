using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 资源租约的生命周期：<b>释放点恰好一次</b>。
    /// <para>少一次即资源泄漏（YooAsset 的引用计数降不下去，资源永远卸载不掉），多一次即重复释放。
    /// 这条只能靠替身记账来断言——每个用例离开时 <see cref="FakeAudioClipLoader.OutstandingLeases"/>
    /// 必须为 <c>0</c>。</para>
    /// </summary>
    [TestFixture]
    public class AudioClipLifetimeTests
    {
        #region Setup

        private FakeAudioClipLoader _loader;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
            UpdateManager.AutoDriveEnabled = true;
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _clip = AudioTestKit.CreateClip();
            _loader = new FakeAudioClipLoader { Clip = _clip };
            AudioManager.SetInstance(new AudioManagerImpl(new AudioInitOptions { MaxVoices = 4 }, _loader));
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
            UpdateManager.Clear();

            if (_clip != null)
                UnityEngine.Object.DestroyImmediate(_clip);
        }

        #endregion

        #region 占用与归还

        [Test]
        public void PlayingVoice_HoldsTheLease()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            Assert.AreEqual(1, _loader.IssuedLeases);
            Assert.AreEqual(0, _loader.ReleasedLeases, "播放期间必须持有租约——它同时是资源的保活凭据");
        }

        [Test]
        public void Stop_ReturnsTheLease()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.Stop(handle);

            Assert.AreEqual(1, _loader.ReleasedLeases);
            Assert.AreEqual(0, _loader.OutstandingLeases);
        }

        [Test]
        public void StopAll_ReturnsLeasesOfEveryVoice()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));
            Assert.AreEqual(2, _loader.OutstandingLeases);

            int stopped = AudioManager.StopAll();

            Assert.AreEqual(2, stopped);
            Assert.AreEqual(0, _loader.OutstandingLeases, "StopAll 必须把每个槽位的租约都还回去");
        }

        [Test]
        public void StopAll_WithChannel_OnlyReturnsLeasesOfThatChannel()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(AudioChannels.Se, loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(AudioChannels.Bgm, loop: true));
            Assert.AreEqual(2, _loader.OutstandingLeases);

            int stopped = AudioManager.StopAll(AudioChannels.Se);

            Assert.AreEqual(1, stopped);
            Assert.AreEqual(1, _loader.OutstandingLeases, "另一个通道的租约不该被牵连");
        }

        [Test]
        public void Destroy_ReturnsLeasesOfEveryVoice()
        {
            AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));
            Assert.AreEqual(2, _loader.OutstandingLeases);

            AudioManager.Destroy();

            Assert.AreEqual(0, _loader.OutstandingLeases, "Destroy 必须把在播的租约全部归还，否则退出时会泄漏");
        }

        #endregion

        #region 加载中的取消路径

        /// <summary>
        /// 加载还没回来就 Stop：槽位立刻作废，<b>已加载的租约由加载续体归还</b>——释放点必须恰好一次，
        /// 既不能在 Stop 里释放（那时还没有租约），也不能在续体里漏掉。
        /// </summary>
        [UnityTest]
        public IEnumerator Stop_WhileLoading_LeaseIsReturnedExactlyOnce()
        {
            _loader.DelayFrames = 3;

            var handle = AudioManager.Play("audio/a");
            Assert.AreEqual(1, _loader.LoadCount, "Play 应已发起加载");
            Assert.AreEqual(0, _loader.IssuedLeases, "此时加载尚未完成");
            Assert.AreEqual(1, AudioManager.ActiveVoiceCount, "加载中的播放也算活跃——它已经占住了槽位");

            Assert.IsTrue(AudioManager.Stop(handle), "加载中的播放也应能被停下");

            yield return null;
            yield return null;
            yield return null;
            yield return null;   // 越过 DelayFrames，让续体跑完

            Assert.AreEqual(1, _loader.ReleasedLeases, "续体必须把已经拿到的租约归还");
            Assert.AreEqual(0, _loader.OutstandingLeases);
            Assert.IsFalse(AudioManager.IsPlaying(handle));
        }

        [UnityTest]
        public IEnumerator Destroy_WhileLoading_DoesNotLeak()
        {
            _loader.DelayFrames = 3;

            AudioManager.Play("audio/a");
            Assert.AreEqual(0, _loader.IssuedLeases);

            AudioManager.Destroy();

            yield return null;
            yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual(0, _loader.OutstandingLeases, "实例销毁后在途加载拿到的租约也必须被归还");
        }

        #endregion

        #region 多次播放互不串账

        [Test]
        public void ConcurrentPlaybacks_TrackTheirOwnLeases()
        {
            var a = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Play("audio/b", new AudioPlayOptions(loop: true));
            Assert.AreEqual(2, _loader.OutstandingLeases);

            AudioManager.Stop(a);

            Assert.AreEqual(1, _loader.OutstandingLeases, "只该归还被停掉那一个的租约");
        }

        #endregion
    }
}
