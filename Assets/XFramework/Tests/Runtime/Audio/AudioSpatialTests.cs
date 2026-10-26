using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// 内置 provider 的逐次 3D 位置：<see cref="AudioManager.SetPosition"/> 直接写播放源 Transform。
    /// </summary>
    [TestFixture]
    public class AudioSpatialTests
    {
        #region Setup

        private AudioClip _clip;
        private FakeAudioClipLoader _loader;

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

        [Test]
        public async Task SetPosition_MovesActive3DSource()
        {
            var handle = await AudioManager.PlayAsync("audio/a",
                new AudioPlayOptions(spatialBlend: 1f, position: Vector3.zero, loop: true));
            var expected = new Vector3(4f, 2f, -6f);

            Assert.IsTrue(AudioManager.SetPosition(handle, expected));
            Assert.AreEqual(expected, AudioTestKit.FindSourceFor(handle).transform.position);
        }

        /// <summary>
        /// 2D 播放没有空间化效果，但公开承诺是「把位置写到播放源」；仍应返回 true 并写入。
        /// </summary>
        [Test]
        public async Task SetPosition_On2DSource_WritesAndReturnsTrue()
        {
            var handle = await AudioManager.PlayAsync("audio/a",
                new AudioPlayOptions(spatialBlend: 0f, loop: true));

            Assert.IsTrue(AudioManager.SetPosition(handle, new Vector3(2f, 0f, 0f)));
            Assert.AreEqual(new Vector3(2f, 0f, 0f),
                AudioTestKit.FindSourceFor(handle).transform.position);
        }

        [Test]
        public async Task SetPosition_OnPausedSource_WritesAndReturnsTrue()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));
            AudioManager.Pause();

            Assert.IsTrue(AudioManager.SetPosition(handle, new Vector3(1f, 0f, 1f)));
            Assert.AreEqual(new Vector3(1f, 0f, 1f),
                AudioTestKit.FindSourceFor(handle).transform.position);
        }

        [Test]
        public async Task SetPosition_AfterStop_ReturnsFalse()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.Stop(handle);

            Assert.IsFalse(AudioManager.SetPosition(handle, Vector3.one));
        }

        [Test]
        public void SetPosition_DefaultHandle_ReturnsFalse()
        {
            Assert.IsFalse(AudioManager.SetPosition(default, Vector3.one));
        }

        [Test]
        public async Task SetPosition_ExternallyDestroyedSource_ReturnsFalse()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));
            var source = AudioTestKit.FindSourceFor(handle);

            UnityEngine.Object.DestroyImmediate(source.gameObject);

            Assert.IsFalse(AudioManager.SetPosition(handle, Vector3.one));
        }
    }
}
