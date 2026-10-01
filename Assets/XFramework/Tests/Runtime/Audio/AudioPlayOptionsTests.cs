using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 播放参数：归一化与落到播放源上的实际值。
    /// <para><b>归一化是本组的重点</b>。省略 <c>options</c> 实参传入的是 <c>default(AudioPlayOptions)</c>
    /// （字段全为零），而「音量为 0、音高为 0」在引擎里意味着一个完全静音的声音——这比抛异常更难查。
    /// 因此非正的 <c>VolumeScale</c> / <c>Pitch</c> 一律按 <c>1</c> 处理，<c>SpatialBlend</c> 钳到 <c>[0,1]</c>。
    /// </para>
    /// </summary>
    [TestFixture]
    public class AudioPlayOptionsTests
    {
        #region Setup

        private FakeAudioClipLoader _loader;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
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

        #region 值语义

        [Test]
        public void Default_HasIntuitiveValues()
        {
            var options = AudioPlayOptions.Default;

            Assert.IsNull(options.Channel, "null 通道归一化为默认通道");
            Assert.AreEqual(1f, options.VolumeScale, 0.001f);
            Assert.IsFalse(options.Loop);
            Assert.AreEqual(1f, options.Pitch, 0.001f);
            Assert.AreEqual(0f, options.SpatialBlend, 0.001f);
            Assert.AreEqual(Vector3.zero, options.Position);
        }

        /// <summary>
        /// <c>default(AudioPlayOptions)</c> 的字段全为零——<b>正是它让归一化成为必需品</b>。
        /// 这条用例把那件事写成断言，免得将来有人「顺手」把归一化删掉。
        /// </summary>
        [Test]
        public void DefaultStruct_HasZeroFields()
        {
            var raw = default(AudioPlayOptions);

            Assert.IsNull(raw.Channel);
            Assert.AreEqual(0f, raw.VolumeScale, 0.001f);
            Assert.IsFalse(raw.Loop);
            Assert.AreEqual(0f, raw.Pitch, 0.001f);
            Assert.AreEqual(0f, raw.SpatialBlend, 0.001f);
        }

        #endregion

        #region 归一化（省略 options 的路径）

        [Test]
        public void Play_WithDefaultStruct_NormalizesPitchAndVolume()
        {
            var handle = AudioManager.Play("audio/a", default);

            var source = AudioTestKit.FindSourceFor(handle);
            Assert.IsNotNull(source);
            Assert.AreEqual(1f, source.pitch, 0.001f, "音高 0 必须归一化成 1，否则声音被拉成静音");
            Assert.AreEqual(1f, source.volume, 0.001f, "音量 0 必须归一化成 1");
            Assert.IsFalse(source.loop);
            Assert.AreEqual(0f, source.spatialBlend, 0.001f);
        }

        [Test]
        public void Play_WithNonPositivePitch_NormalizesToOne()
        {
            var zero = AudioManager.Play("audio/a", new AudioPlayOptions(pitch: 0f, loop: true));
            var negative = AudioManager.Play("audio/b", new AudioPlayOptions(pitch: -2f, loop: true));

            Assert.AreEqual(1f, AudioTestKit.FindSourceFor(zero).pitch, 0.001f);
            Assert.AreEqual(1f, AudioTestKit.FindSourceFor(negative).pitch, 0.001f);
        }

        #endregion

        #region 正常传值

        [Test]
        public void Pitch_IsAppliedToSource()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(pitch: 1.5f, loop: true));

            Assert.AreEqual(1.5f, AudioTestKit.FindSourceFor(handle).pitch, 0.001f);
        }

        [Test]
        public void Loop_IsAppliedToSource()
        {
            var looping = AudioManager.Play("audio/a", new AudioPlayOptions(loop: true));
            var oneShot = AudioManager.Play("audio/b", new AudioPlayOptions(loop: false));

            Assert.IsTrue(AudioTestKit.FindSourceFor(looping).loop);
            Assert.IsFalse(AudioTestKit.FindSourceFor(oneShot).loop);
        }

        [Test]
        public void SpatialBlend_IsAppliedToSource()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(spatialBlend: 0.5f, loop: true));

            Assert.AreEqual(0.5f, AudioTestKit.FindSourceFor(handle).spatialBlend, 0.001f);
        }

        [Test]
        public void SpatialBlend_AboveOne_IsClamped()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(spatialBlend: 3f, loop: true));

            Assert.AreEqual(1f, AudioTestKit.FindSourceFor(handle).spatialBlend, 0.001f);
        }

        [Test]
        public void SpatialBlend_BelowZero_IsClamped()
        {
            var handle = AudioManager.Play("audio/a", new AudioPlayOptions(spatialBlend: -1f, loop: true));

            Assert.AreEqual(0f, AudioTestKit.FindSourceFor(handle).spatialBlend, 0.001f);
        }

        #endregion

        #region 3D 位置

        [Test]
        public void Position_IsAppliedWhenSpatialBlendIsPositive()
        {
            var expected = new Vector3(3f, 0f, -7f);
            var handle = AudioManager.Play("audio/a",
                new AudioPlayOptions(spatialBlend: 1f, position: expected, loop: true));

            Assert.AreEqual(expected, AudioTestKit.FindSourceFor(handle).transform.position);
        }

        /// <summary>
        /// 纯 2D 时不写位置：写了也没意义，还会把槽位的 Transform 挪到一个与本次播放无关的地方。
        /// </summary>
        [Test]
        public void Position_IsIgnoredWhenPure2D()
        {
            var handle = AudioManager.Play("audio/a",
                new AudioPlayOptions(spatialBlend: 0f, position: new Vector3(100f, 0f, 0f), loop: true));

            Assert.AreEqual(Vector3.zero, AudioTestKit.FindSourceFor(handle).transform.position);
        }

        #endregion

        #region 与 Asset 地址无关的防御

        [Test]
        public void Play_WithReservedChannel_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => AudioManager.Play("audio/a", new AudioPlayOptions(AudioManagerImpl.ReservedMasterChannel)));
        }

        #endregion
    }
}
