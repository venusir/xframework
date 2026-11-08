using System;
using FMOD.Studio;
using NUnit.Framework;
using XFramework.XAudio;

namespace XFramework.XAudio.Fmod.Unity.Tests
{
    /// <summary>
    /// 真实 FMOD backend 的纯状态映射、无效句柄与幂等测试；真实事件/bank 冒烟由
    /// <c>XFW_FMOD_EVENT</c> / <c>XFW_FMOD_BANK</c> 环境变量门控。
    /// </summary>
    [TestFixture]
    public class FmodEventBackendTests
    {
        [Test]
        public void MapState_MapsAllStates()
        {
            Assert.AreEqual(AudioProviderPlaybackState.Invalid,
                FmodEventBackend.MapState(false, (PLAYBACK_STATE)99));
            Assert.AreEqual(AudioProviderPlaybackState.Starting,
                FmodEventBackend.MapState(false, PLAYBACK_STATE.STARTING));
            Assert.AreEqual(AudioProviderPlaybackState.Playing,
                FmodEventBackend.MapState(false, PLAYBACK_STATE.PLAYING));
            Assert.AreEqual(AudioProviderPlaybackState.Playing,
                FmodEventBackend.MapState(false, PLAYBACK_STATE.SUSTAINING));
            Assert.AreEqual(AudioProviderPlaybackState.Playing,
                FmodEventBackend.MapState(false, PLAYBACK_STATE.STOPPING));
            Assert.AreEqual(AudioProviderPlaybackState.Stopped,
                FmodEventBackend.MapState(false, PLAYBACK_STATE.STOPPED));
            Assert.AreEqual(AudioProviderPlaybackState.Paused,
                FmodEventBackend.MapState(true, PLAYBACK_STATE.PLAYING));
            Assert.AreEqual(AudioProviderPlaybackState.Paused,
                FmodEventBackend.MapState(true, PLAYBACK_STATE.STOPPING));
            Assert.AreEqual(AudioProviderPlaybackState.Stopped,
                FmodEventBackend.MapState(true, PLAYBACK_STATE.STOPPED));
        }

        [Test]
        public void Poll_UnknownId_ReturnsInvalid()
        {
            var backend = new FmodEventBackend(false);
            try
            {
                Assert.AreEqual(AudioProviderPlaybackState.Invalid, backend.Poll(123));
            }
            finally
            {
                backend.Dispose();
            }
        }

        [Test]
        public void Release_UnknownId_IsIdempotent()
        {
            var backend = new FmodEventBackend(false);
            try
            {
                backend.Release(123);
                backend.Release(123);
            }
            finally
            {
                backend.Dispose();
            }
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var backend = new FmodEventBackend(false);
            backend.Dispose();
            backend.Dispose();
        }

        [Test]
        public void BusSetters_AfterDispose_ReturnFalse()
        {
            var backend = new FmodEventBackend(false);
            backend.Dispose();

            Assert.IsFalse(backend.SetBusVolume("bus:/SFX", 1f));
            Assert.IsFalse(backend.SetBusMuted("bus:/SFX", true));
            Assert.IsFalse(backend.SetBusPaused("bus:/SFX", true));
        }

        [Test]
        public void RealEvent_StartStopRelease_WhenConfigured()
        {
            string eventPath = Environment.GetEnvironmentVariable("XFW_FMOD_EVENT");
            if (string.IsNullOrWhiteSpace(eventPath))
                Assert.Ignore("未配置 XFW_FMOD_EVENT；跳过真实 FMOD 事件冒烟。");

            var backend = new FmodEventBackend(false);
            try
            {
                int id = backend.Create(eventPath);
                Assert.AreNotEqual(0, id, "FMOD 事件创建失败；请确认 FMOD Setup Wizard 与 Studio 工程已配置。");

                Assert.IsTrue(backend.Start(id));
                Assert.IsTrue(backend.SetVolume(id, 0.5f));
                Assert.IsTrue(backend.SetPitch(id, 1f));
                // 参数名由 Studio 工程决定；存在性不作为冒烟门槛，只验证调用不抛异常。
                backend.SetParameter(id, new AudioParameter("XFwProbe", 0.5f));
                Assert.IsTrue(backend.SetPaused(id, true));
                Assert.AreEqual(AudioProviderPlaybackState.Paused, backend.Poll(id));
                Assert.IsTrue(backend.SetPaused(id, false));
                Assert.IsTrue(backend.Stop(id));
                backend.Release(id);
            }
            finally
            {
                backend.Dispose();
            }
        }

        [Test]
        public void RealBank_LoadUnload_WhenConfigured()
        {
            string bank = Environment.GetEnvironmentVariable("XFW_FMOD_BANK");
            if (string.IsNullOrWhiteSpace(bank))
                Assert.Ignore("未配置 XFW_FMOD_BANK；跳过真实 FMOD bank 冒烟。");

            var backend = new FmodEventBackend(false);
            try
            {
                if (backend.IsBankLoaded(bank))
                    Assert.Ignore("bank 已由项目预加载；为避免卸载项目 bank，跳过直接冒烟。");

                Assert.IsTrue(backend.LoadBank(bank, false));
                Assert.IsTrue(backend.IsBankLoaded(bank));
                Assert.IsTrue(backend.UnloadBank(bank));
                Assert.IsFalse(backend.IsBankLoaded(bank));
            }
            finally
            {
                backend.Dispose();
            }
        }
    }
}
