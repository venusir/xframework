using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 生命周期与句柄安全语义。
    /// <para><b>本 fixture 的划分原则</b>：只覆盖「门面还没实现播放内核也能验证」的部分——初始化 / 销毁 /
    /// 未初始化异常形态 / <b>探测型成员的宽松语义</b>。播放、代际、音量各自的用例在后续提交里另开 fixture。</para>
    /// <para><b>探测型成员</b>指 <see cref="AudioManager.Stop"/> 与 <see cref="AudioManager.IsPlaying"/>：
    /// 句柄的生命周期可以长于管理器，它们必须安全返回 <c>false</c> 而不是抛。</para>
    /// </summary>
    [TestFixture]
    public class AudioLifecycleTests
    {
        #region Setup

        [SetUp]
        public void SetUp()
        {
            // 门面是静态的：PlayMode 下所有用例共享一个 player 实例，不复位即互相污染
            AudioManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
        }

        #endregion

        #region Initialize / Destroy

        [Test]
        public void IsInitialized_BeforeInitialize_IsFalse()
        {
            Assert.IsFalse(AudioManager.IsInitialized);
        }

        [Test]
        public void Initialize_SetsIsInitialized()
        {
            AudioManager.Initialize();

            Assert.IsTrue(AudioManager.IsInitialized);
        }

        [Test]
        public void Initialize_CalledTwice_WarnsAndKeepsTheExistingInstance()
        {
            var fake = new FakeAudioManager();
            AudioManager.SetInstance(fake);

            LogAssert.Expect(LogType.Warning, "[Audio] Initialize was called more than once. Ignoring duplicate.");
            AudioManager.Initialize();

            // 若重复 Initialize 真的重建了实例，门面持有的就不是 fake，Destroy 也就不会 Dispose 它
            AudioManager.Destroy();
            Assert.IsTrue(fake.Disposed, "重复 Initialize 应被忽略，门面持有的仍应是先前注入的实例");
        }

        [Test]
        public void Destroy_ResetsIsInitialized()
        {
            AudioManager.Initialize();

            AudioManager.Destroy();

            Assert.IsFalse(AudioManager.IsInitialized);
        }

        [Test]
        public void Destroy_WhenNotInitialized_IsSafe()
        {
            AudioManager.Destroy();
            AudioManager.Destroy();

            Assert.IsFalse(AudioManager.IsInitialized);
        }

        #endregion

        #region SetInstance

        [Test]
        public void SetInstance_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AudioManager.SetInstance(null));
        }

        [Test]
        public void SetInstance_SetsIsInitialized()
        {
            AudioManager.SetInstance(new FakeAudioManager());

            Assert.IsTrue(AudioManager.IsInitialized);
        }

        [Test]
        public void Destroy_DisposesInjectedInstance()
        {
            var fake = new FakeAudioManager();
            AudioManager.SetInstance(fake);

            AudioManager.Destroy();

            Assert.IsTrue(fake.Disposed, "Destroy 必须 Dispose 注入的实例——它自持的宿主与播放源池要跟着释放");
        }

        #endregion

        #region 未初始化时的成员语义

        /// <summary>
        /// 管理器范围的成员（非探测型）在未初始化时一律抛 <see cref="InvalidOperationException"/>，
        /// 消息带 <c>[Audio]</c> 前缀与修复提示。
        /// </summary>
        [Test]
        public void ManagerScopedMembers_WhenUninitialized_ThrowInvalidOperation()
        {
            AssertAudioException(() => { AudioManager.Play("audio/x"); });
            AssertAudioException(() => { AudioManager.PlayAsync("audio/x"); });
            AssertAudioException(() => { AudioManager.StopAll(); });
            AssertAudioException(() => { AudioManager.Pause(); });
            AssertAudioException(() => { AudioManager.Resume(); });
            AssertAudioException(() => { var _ = AudioManager.IsPaused; });
            AssertAudioException(() => { var _ = AudioManager.MasterVolume; });
            AssertAudioException(() => { AudioManager.MasterVolume = 0.5f; });
            AssertAudioException(() => { var _ = AudioManager.MasterMuted; });
            AssertAudioException(() => { AudioManager.SetChannelVolume(AudioChannels.Se, 0.5f); });
            AssertAudioException(() => { var _ = AudioManager.GetChannelVolume(AudioChannels.Se); });
            AssertAudioException(() => { AudioManager.SetChannelMuted(AudioChannels.Se, true); });
            AssertAudioException(() => { var _ = AudioManager.IsChannelMuted(AudioChannels.Se); });
            AssertAudioException(() => { AudioManager.RegisterChannel(AudioChannels.Se, new AudioChannelConfig()); });
            AssertAudioException(() => { var _ = AudioManager.ActiveVoiceCount; });
            AssertAudioException(() => { var _ = AudioManager.GetActiveVoiceCount(AudioChannels.Se); });
        }

        /// <summary>
        /// 探测型成员在未初始化时返回 <c>false</c>，不抛。
        /// </summary>
        [Test]
        public void ProbeMembers_WhenUninitialized_ReturnFalse()
        {
            Assert.IsFalse(AudioManager.Stop(default));
            Assert.IsFalse(AudioManager.IsPlaying(default));
            Assert.IsFalse(AudioManager.Stop(new AudioHandle(3, 1)));
            Assert.IsFalse(AudioManager.IsPlaying(new AudioHandle(3, 1)));
        }

        /// <summary>
        /// 句柄可以长于管理器：<see cref="AudioManager.Destroy"/> 之后，先前拿到的句柄仍然安全。
        /// <para>这是本模块刻意保留的宽松语义——销毁时把使用方手上的句柄变成「一碰就抛的地雷」，
        /// 会让「退出时统一清理」这类代码不得不处处加保护。</para>
        /// </summary>
        [Test]
        public void ProbeMembers_AfterDestroy_ReturnFalse()
        {
            AudioManager.Initialize();
            var handle = new AudioHandle(0, 1);

            AudioManager.Destroy();

            Assert.IsFalse(AudioManager.Stop(handle));
            Assert.IsFalse(AudioManager.IsPlaying(handle));
        }

        #endregion

        #region 句柄的值语义

        [Test]
        public void Handle_GenerationZero_IsDefault()
        {
            Assert.IsTrue(default(AudioHandle).IsDefault);
            Assert.IsTrue(new AudioHandle(0, 0).IsDefault);
            Assert.IsTrue(new AudioHandle(7, 0).IsDefault);
        }

        [Test]
        public void Handle_NonZeroGeneration_IsNotDefault()
        {
            Assert.IsFalse(new AudioHandle(0, 1).IsDefault);
            Assert.IsFalse(new AudioHandle(7, 2).IsDefault);
        }

        /// <summary>
        /// 默认句柄上的 <see cref="AudioHandle.Stop"/> / <see cref="AudioHandle.Dispose"/> 是空操作：
        /// 它们必须在<b>门面从未初始化</b>时也安全——这正是「池已满/加载失败返回 default」这条契约
        /// 不会把调用方拖进异常的前提。
        /// </summary>
        [Test]
        public void Handle_Default_StopAndDispose_AreSafe()
        {
            Assert.IsFalse(default(AudioHandle).Stop());

            var handle = default(AudioHandle);
            handle.Dispose();
        }

        #endregion

        #region Helpers

        private static void AssertAudioException(TestDelegate action)
        {
            var ex = Assert.Throws<InvalidOperationException>(action);

            StringAssert.Contains("[Audio]", ex.Message, "未初始化异常的消息必须带 [Audio] 前缀");
            StringAssert.Contains("Initialize", ex.Message, "异常消息必须给出修复提示");
        }

        #endregion

        #region Fakes

        /// <summary>
        /// 只记录 <see cref="Dispose"/> 的最小实现。C1 的生命周期用例不碰播放行为，故其余成员一律返回中性值。
        /// </summary>
        private sealed class FakeAudioManager : IAudioManager
        {
            internal bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;

            public AudioHandle Play(string location, AudioPlayOptions options = default) => default;

            public UniTask<AudioHandle> PlayAsync(string location, AudioPlayOptions options = default,
                CancellationToken cancellationToken = default) => UniTask.FromResult(default(AudioHandle));

            public bool Stop(AudioHandle handle) => false;

            public int StopAll(string channel = null) => 0;

            public void Pause()
            {
            }

            public void Resume()
            {
            }

            public bool IsPaused => false;

            public float MasterVolume { get; set; } = 1f;

            public bool MasterMuted { get; set; }

            public void SetChannelVolume(string channel, float volume)
            {
            }

            public float GetChannelVolume(string channel) => 1f;

            public void SetChannelMuted(string channel, bool muted)
            {
            }

            public bool IsChannelMuted(string channel) => false;

            public void RegisterChannel(string channel, AudioChannelConfig config)
            {
            }

            public bool IsPlaying(AudioHandle handle) => false;

            public int ActiveVoiceCount => 0;

            public int GetActiveVoiceCount(string channel) => 0;
        }

        #endregion
    }
}
