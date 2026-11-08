using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XAudio;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// <see cref="FmodAudioProvider"/> 的播放原语：请求映射、起播前参数、失败回滚、槽位代际、
    /// Poll 映射与 Dispose。
    /// </summary>
    [TestFixture]
    public class FmodAudioProviderTests
    {
        #region Setup

        private FakeFmodEventBackend _backend;
        private FmodAudioProvider _provider;
        private CancellationTokenSource _validityCts;

        [SetUp]
        public void SetUp()
        {
            _backend = new FakeFmodEventBackend();
            _provider = new FmodAudioProvider(_backend, new FmodProviderOptions("event:/", 4));
            _validityCts = new CancellationTokenSource();
        }

        [TearDown]
        public void TearDown()
        {
            _validityCts.Cancel();
            _validityCts.Dispose();
            _provider.Dispose();
        }

        private AudioPlaybackRequest Request(
            float volume = 1f,
            float pitch = 1f,
            float spatialBlend = 0f,
            Vector3 position = default,
            bool paused = false,
            AudioParameter[] initialParameters = null,
            CancellationToken? validity = null)
        {
            return new AudioPlaybackRequest(
                false,
                pitch,
                spatialBlend,
                position,
                volume,
                paused,
                validity ?? _validityCts.Token,
                initialParameters);
        }

        #endregion

        #region 起播映射

        [Test]
        public async Task PlayAsync_CreatesSetsAndStarts()
        {
            var handle = await _provider.PlayAsync("event:/SFX/Footstep",
                Request(volume: 0.5f, pitch: 1.25f));

            Assert.IsFalse(handle.IsDefault);
            Assert.AreEqual("event:/SFX/Footstep", _backend.LastEventPath);
            Assert.AreEqual(0.5f, _backend.LastVolume, 0.001f);
            Assert.AreEqual(1.25f, _backend.LastPitch, 0.001f);
            Assert.AreEqual(1, _backend.StartCalls);

            int create = _backend.Calls.FindIndex(c => c.StartsWith("Create:"));
            int volume = _backend.Calls.FindIndex(c => c.StartsWith("SetVolume:"));
            int pitch = _backend.Calls.FindIndex(c => c.StartsWith("SetPitch:"));
            int start = _backend.Calls.FindIndex(c => c.StartsWith("Start:"));
            Assert.Less(create, volume);
            Assert.Less(volume, start);
            Assert.Less(pitch, start);
        }

        [Test]
        public async Task PlayAsync_PrependsPrefixOnlyWhenMissing()
        {
            await _provider.PlayAsync("SFX/Footstep", Request());
            Assert.AreEqual("event:/SFX/Footstep", _backend.LastEventPath);

            await _provider.PlayAsync("event:/AlreadyPrefixed", Request());
            Assert.AreEqual("event:/AlreadyPrefixed", _backend.LastEventPath);
        }

        [Test]
        public async Task PlayAsync_NullLocation_ReturnsDefaultWithoutConsumingSlot()
        {
            var provider = new FmodAudioProvider(_backend, new FmodProviderOptions("event:/", 1));
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("location"));

                var failed = await provider.PlayAsync(null, Request());
                Assert.IsTrue(failed.IsDefault);
                Assert.AreEqual(0, _backend.CreateCalls);

                var handle = await provider.PlayAsync("event:/ok", Request());
                Assert.IsFalse(handle.IsDefault, "空 location 不应占用槽位。");
            }
            finally
            {
                provider.Dispose();
            }
        }

        [Test]
        public async Task PlayAsync_EmptyOrWhitespaceLocation_ReturnsDefaultWithoutCreating()
        {
            LogAssert.Expect(LogType.Warning, new Regex("location"));
            var empty = await _provider.PlayAsync(string.Empty, Request());
            Assert.IsTrue(empty.IsDefault);

            LogAssert.Expect(LogType.Warning, new Regex("location"));
            var whitespace = await _provider.PlayAsync("   ", Request());
            Assert.IsTrue(whitespace.IsDefault);

            Assert.AreEqual(0, _backend.CreateCalls);
        }

        [Test]
        public async Task PlayAsync_AppliesInitialParametersBeforeStart()
        {
            var parameters = new[]
            {
                new AudioParameter("Health", 0.5f),
                new AudioParameter("Surface", "Grass"),
            };

            var handle = await _provider.PlayAsync("event:/SFX/Footstep",
                Request(initialParameters: parameters));

            Assert.IsFalse(handle.IsDefault);
            Assert.AreEqual(2, _backend.Parameters.Count);
            Assert.AreEqual("Health", _backend.Parameters[0].Name);
            Assert.AreEqual("Grass", _backend.Parameters[1].Label);

            int start = _backend.Calls.FindIndex(c => c.StartsWith("Start:"));
            int firstParameter = _backend.Calls.FindIndex(c => c.StartsWith("SetParameter:"));
            Assert.Less(firstParameter, start, "起播前参数必须在 start 之前应用");
        }

        [Test]
        public async Task PlayAsync_ParameterFailureDoesNotBlockStart()
        {
            _backend.FailSetParameter = true;
            LogAssert.Expect(LogType.Warning, new Regex("的参数 'Health' 应用失败，已忽略"));

            var handle = await _provider.PlayAsync("event:/SFX/Footstep",
                Request(initialParameters: new[] { new AudioParameter("Health", 0.5f) }));

            Assert.IsFalse(handle.IsDefault);
            Assert.AreEqual(1, _backend.StartCalls);
        }

        [Test]
        public async Task PlayAsync_SpatialBlendZero_DoesNotWrite3D()
        {
            await _provider.PlayAsync("event:/SFX/Footstep",
                Request(spatialBlend: 0f, position: new Vector3(3f, 0f, 0f)));

            Assert.AreEqual(0, _backend.Set3DAttributesCalls);
        }

        [Test]
        public async Task PlayAsync_SpatialBlendPositive_WritesPosition()
        {
            var position = new Vector3(3f, 1f, -2f);

            await _provider.PlayAsync("event:/SFX/Footstep",
                Request(spatialBlend: 1f, position: position));

            Assert.AreEqual(1, _backend.Set3DAttributesCalls);
            Assert.AreEqual(position, _backend.LastPosition);
        }

        [Test]
        public async Task PlayAsync_Paused_SetsPauseAfterStart()
        {
            await _provider.PlayAsync("event:/SFX/Footstep", Request(paused: true));

            Assert.AreEqual(1, _backend.SetPausedCalls);
            int start = _backend.Calls.FindIndex(c => c.StartsWith("Start:"));
            int paused = _backend.Calls.FindIndex(c => c.StartsWith("SetPaused:"));
            Assert.Less(start, paused);
        }

        #endregion

        #region 取消与失败回滚

        [Test]
        public void PlayAsync_CallerCancellation_ThrowsWithoutCreating()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
            {
                _provider.PlayAsync("event:/SFX/Footstep", Request(), cts.Token);
            });
            Assert.AreEqual(0, _backend.CreateCalls);
        }

        [Test]
        public async Task PlayAsync_ValidityCanceled_ReturnsDefaultWithoutCreating()
        {
            using var validity = new CancellationTokenSource();
            validity.Cancel();

            var handle = await _provider.PlayAsync("event:/SFX/Footstep",
                Request(validity: validity.Token));

            Assert.IsTrue(handle.IsDefault);
            Assert.AreEqual(0, _backend.CreateCalls);
        }

        [Test]
        public async Task PlayAsync_CreateFailure_ReleasesSlotForRetry()
        {
            _backend.FailCreate = true;
            LogAssert.Expect(LogType.Warning, new Regex("创建失败"));

            var failed = await _provider.PlayAsync("event:/SFX/Footstep", Request());
            Assert.IsTrue(failed.IsDefault);

            _backend.FailCreate = false;
            var handle = await _provider.PlayAsync("event:/SFX/Footstep", Request());
            Assert.IsFalse(handle.IsDefault, "创建失败必须归还槽位");
        }

        [Test]
        public async Task PlayAsync_StartFailure_ReleasesBackendInstance()
        {
            _backend.FailStart = true;
            LogAssert.Expect(LogType.Warning, new Regex("起播失败"));

            var handle = await _provider.PlayAsync("event:/SFX/Footstep", Request());

            Assert.IsTrue(handle.IsDefault);
            Assert.AreEqual(1, _backend.ReleasedIds.Count);
            Assert.AreEqual(_backend.LastCreatedId, _backend.ReleasedIds[0]);
        }

        [Test]
        public async Task PlayAsync_VolumeFailure_ReleasesBackendInstance()
        {
            _backend.FailSetVolume = true;
            LogAssert.Expect(LogType.Warning, new Regex("播放参数设置失败"));

            var handle = await _provider.PlayAsync("event:/SFX/Footstep", Request());

            Assert.IsTrue(handle.IsDefault);
            Assert.AreEqual(1, _backend.ReleasedIds.Count);
        }

        [Test]
        public async Task PlayAsync_BackendCreateThrows_IsIsolatedAndSlotIsReusable()
        {
            _backend.ThrowOnCreate = true;
            LogAssert.Expect(LogType.Error, new Regex("PlayAsync 调用抛出异常"));

            var failed = await _provider.PlayAsync("event:/SFX/Footstep", Request());
            Assert.IsTrue(failed.IsDefault);

            _backend.ThrowOnCreate = false;
            var handle = await _provider.PlayAsync("event:/SFX/Footstep", Request());
            Assert.IsFalse(handle.IsDefault);
        }

        [Test]
        public async Task PlayAsync_StartThrows_IsIsolatedAndReleases()
        {
            _backend.ThrowOnStart = true;
            LogAssert.Expect(LogType.Error, new Regex("PlayAsync 调用抛出异常"));

            var handle = await _provider.PlayAsync("event:/SFX/Footstep", Request());

            Assert.IsTrue(handle.IsDefault);
            Assert.AreEqual(1, _backend.ReleasedIds.Count);
        }

        #endregion

        #region 容量与代际

        [Test]
        public async Task PlayAsync_CapacityFull_ReturnsDefaultWithoutCreating()
        {
            using var backend = new FakeFmodEventBackend();
            var provider = new FmodAudioProvider(backend, new FmodProviderOptions("event:/", 1));

            var first = await provider.PlayAsync("event:/a", Request());
            Assert.IsFalse(first.IsDefault);

            LogAssert.Expect(LogType.Warning, new Regex("槽位已满"));
            var second = await provider.PlayAsync("event:/b", Request());
            Assert.IsTrue(second.IsDefault);
            Assert.AreEqual(1, backend.CreateCalls);

            provider.Release(first);
            var third = await provider.PlayAsync("event:/c", Request());
            Assert.IsFalse(third.IsDefault);

            provider.Dispose();
        }

        [Test]
        public async Task SlotReuse_IncrementsGenerationAndInvalidatesOldHandle()
        {
            var first = await _provider.PlayAsync("event:/a", Request());
            _provider.Release(first);
            var second = await _provider.PlayAsync("event:/b", Request());

            Assert.AreEqual(first.Id, second.Id, "空闲槽位会被复用");
            Assert.AreNotEqual(first.Generation, second.Generation);

            Assert.IsFalse(_provider.Stop(first));
            Assert.AreEqual(AudioProviderPlaybackState.Invalid, _provider.Poll(first));
            _provider.Release(first);
            Assert.AreEqual(1, _backend.ReleasedIds.Count, "旧句柄的 Release 不得再释放 backend 实例");
        }

        #endregion

        #region 控制原语

        [Test]
        public async Task Stop_DoesNotRelease_ReleaseIsIdempotent()
        {
            var handle = await _provider.PlayAsync("event:/a", Request());

            Assert.IsTrue(_provider.Stop(handle));
            Assert.AreEqual(1, _backend.StopCalls);
            Assert.AreEqual(0, _backend.ReleasedIds.Count, "Stop 只停播，不释放");

            _provider.Release(handle);
            _provider.Release(handle);
            Assert.AreEqual(1, _backend.ReleasedIds.Count, "Release 必须幂等");
        }

        [Test]
        public async Task SetPausedAndVolume_ForwardAndFailAfterRelease()
        {
            var handle = await _provider.PlayAsync("event:/a", Request());

            Assert.IsTrue(_provider.SetPaused(handle, true));
            Assert.IsTrue(_provider.SetVolume(handle, 0.25f));
            Assert.AreEqual(1, _backend.SetPausedCalls);
            Assert.AreEqual(0.25f, _backend.LastVolume, 0.001f);

            _provider.Release(handle);

            Assert.IsFalse(_provider.SetPaused(handle, false));
            Assert.IsFalse(_provider.SetVolume(handle, 1f));
        }

        [Test]
        public async Task Poll_MapsKnownStatesAndNormalizesUnknown()
        {
            var handle = await _provider.PlayAsync("event:/a", Request());

            foreach (var state in new[]
                     {
                         AudioProviderPlaybackState.Starting,
                         AudioProviderPlaybackState.Playing,
                         AudioProviderPlaybackState.Paused,
                         AudioProviderPlaybackState.Stopped,
                     })
            {
                _backend.PollState = state;
                Assert.AreEqual(state, _provider.Poll(handle));
            }

            _backend.PollUnknown = true;
            Assert.AreEqual(AudioProviderPlaybackState.Invalid, _provider.Poll(handle));
            Assert.AreEqual(AudioProviderPlaybackState.Invalid, _provider.Poll(default));
        }

        #endregion

        #region 能力接口

        [Test]
        public async Task Capabilities_ParameterGlobalAndPosition_Forward()
        {
            var handle = await _provider.PlayAsync("event:/a", Request());
            var parameters = (IAudioParameterProvider)_provider;
            var spatial = (IAudioSpatialProvider)_provider;
            var position = new Vector3(2f, 1f, -3f);

            Assert.IsTrue(parameters.TrySetParameter(handle, new AudioParameter("Health", 0.5f)));
            Assert.IsTrue(parameters.TrySetGlobalParameter(new AudioParameter("Weather", "Rain")));
            Assert.IsTrue(spatial.TrySetPosition(handle, position));

            Assert.AreEqual(1, _backend.Parameters.Count);
            Assert.AreEqual("Health", _backend.Parameters[0].Name);
            Assert.AreEqual(1, _backend.GlobalParameters.Count);
            Assert.AreEqual("Rain", _backend.GlobalParameters[0].Label);
            Assert.AreEqual(1, _backend.Set3DAttributesCalls);
            Assert.AreEqual(position, _backend.LastPosition);
        }

        [Test]
        public async Task Capabilities_BankBusSnapshot_Forward()
        {
            var handle = await _provider.PlayAsync("event:/a", Request());
            var bank = (IAudioBankProvider)_provider;
            var bus = (IAudioBusProvider)_provider;
            var snapshot = (IAudioSnapshotProvider)_provider;

            Assert.IsTrue(bank.TryLoadBank("Master", true));
            Assert.IsTrue(bank.IsBankLoaded("Master"));
            Assert.IsTrue(bank.TryUnloadBank("Master"));

            Assert.IsTrue(bus.TrySetBusVolume("bus:/SFX", 1.5f));
            Assert.IsTrue(bus.TrySetBusMuted("bus:/SFX", true));
            Assert.IsTrue(bus.TrySetBusPaused("bus:/SFX", true));

            Assert.IsTrue(snapshot.TryStartSnapshot("snapshot:/Pause"));
            Assert.IsTrue(snapshot.TryStopSnapshot("snapshot:/Pause"));

            Assert.AreEqual(1, _backend.BankLoads.Count);
            Assert.IsTrue(_backend.BankLoads[0].LoadSamples);
            Assert.AreEqual(1, _backend.BankUnloads.Count);
            Assert.AreEqual(1.5f, _backend.BusVolumes[0].Volume, 0.001f);
            Assert.AreEqual(1, _backend.BusMutes.Count);
            Assert.AreEqual(1, _backend.BusPauses.Count);
            Assert.AreEqual(1, _backend.StartedSnapshots.Count);
            Assert.AreEqual(1, _backend.StoppedSnapshots.Count);

            _provider.Release(handle);
        }

        [Test]
        public async Task Capabilities_InvalidOrReleasedHandle_ReturnFalse()
        {
            var handle = await _provider.PlayAsync("event:/a", Request());
            var parameters = (IAudioParameterProvider)_provider;
            var spatial = (IAudioSpatialProvider)_provider;

            Assert.IsFalse(parameters.TrySetParameter(default, new AudioParameter("Health", 1f)));
            Assert.IsFalse(spatial.TrySetPosition(default, Vector3.one));

            _provider.Release(handle);

            Assert.IsFalse(parameters.TrySetParameter(handle, new AudioParameter("Health", 1f)));
            Assert.IsFalse(spatial.TrySetPosition(handle, Vector3.one));
        }

        [Test]
        public async Task Capabilities_AfterDispose_ReturnFalse()
        {
            await _provider.PlayAsync("event:/a", Request());
            _provider.Dispose();

            Assert.IsFalse(((IAudioParameterProvider)_provider)
                .TrySetGlobalParameter(new AudioParameter("Weather", 1f)));
            Assert.IsFalse(((IAudioBankProvider)_provider).TryLoadBank("Master", false));
            Assert.IsFalse(((IAudioBusProvider)_provider).TrySetBusVolume("bus:/SFX", 1f));
            Assert.IsFalse(((IAudioSnapshotProvider)_provider).TryStartSnapshot("snapshot:/Pause"));
        }

        [Test]
        public async Task Capabilities_BackendThrows_AreIsolated()
        {
            var handle = await _provider.PlayAsync("event:/a", Request());
            _backend.ThrowOnSetParameter = true;

            LogAssert.Expect(LogType.Error, new Regex("SetParameter 调用抛出异常"));

            Assert.IsFalse(((IAudioParameterProvider)_provider)
                .TrySetParameter(handle, new AudioParameter("Health", 1f)));
        }

        #endregion

        #region Dispose

        [Test]
        public async Task Dispose_ReleasesAllAndBackend_IsIdempotent()
        {
            await _provider.PlayAsync("event:/a", Request());
            await _provider.PlayAsync("event:/b", Request());

            _provider.Dispose();

            Assert.AreEqual(2, _backend.ReleasedIds.Count);
            Assert.IsTrue(_backend.Disposed);
            Assert.AreEqual(1, _backend.DisposeCalls);

            _provider.Dispose();
            Assert.AreEqual(1, _backend.DisposeCalls, "Dispose 必须幂等");

            var after = await _provider.PlayAsync("event:/c", Request());
            Assert.IsTrue(after.IsDefault);
        }

        [Test]
        public async Task Dispose_BackendThrows_IsIsolated()
        {
            await _provider.PlayAsync("event:/a", Request());
            _backend.ThrowOnRelease = true;
            _backend.ThrowOnDispose = true;

            LogAssert.Expect(LogType.Error, new Regex("Dispose/Release 调用抛出异常"));
            LogAssert.Expect(LogType.Error, new Regex("Dispose 调用抛出异常"));

            Assert.DoesNotThrow(() => _provider.Dispose());
            Assert.IsTrue(_backend.Disposed);
        }

        #endregion
    }
}
