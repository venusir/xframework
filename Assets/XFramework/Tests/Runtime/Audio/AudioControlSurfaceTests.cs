using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// provider 中立控制面：逐次参数、全局参数与 3D 位置的转发、探测语义与异常隔离。
    /// </summary>
    [TestFixture]
    public class AudioControlSurfaceTests
    {
        #region Setup

        private FakeCapabilityProvider _fake;

        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _fake = new FakeCapabilityProvider();
            AudioManager.SetProvider(_fake, new AudioInitOptions { MaxVoices = 4 });
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
            UpdateManager.Clear();
        }

        #endregion

        #region 逐次参数

        [Test]
        public async Task SetParameter_ForwardsNumericAndLabel()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsTrue(AudioManager.SetParameter(handle, "Health", 0.25f));
            Assert.IsTrue(AudioManager.SetParameter(handle, "Surface", "Grass"));

            Assert.AreEqual(2, _fake.Parameters.Count);
            Assert.AreEqual("Health", _fake.Parameters[0].Parameter.Name);
            Assert.AreEqual(0.25f, _fake.Parameters[0].Parameter.Value, 0.001f);
            Assert.AreEqual("Surface", _fake.Parameters[1].Parameter.Name);
            Assert.AreEqual("Grass", _fake.Parameters[1].Parameter.Label);
        }

        [Test]
        public async Task SetParameter_RejectedByProvider_ReturnsFalse()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));
            _fake.RejectParameter = true;

            Assert.IsFalse(AudioManager.SetParameter(handle, "Health", 1f));
        }

        [Test]
        public async Task SetParameter_AfterStop_ReturnsFalse()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));

            AudioManager.Stop(handle);

            Assert.IsFalse(AudioManager.SetParameter(handle, "Health", 1f));
        }

        [Test]
        public void SetParameter_InvalidName_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => AudioManager.SetParameter(default, null, 1f));
            Assert.Throws<System.ArgumentException>(
                () => AudioManager.SetParameter(default, " ", 1f));
            Assert.Throws<System.ArgumentException>(
                () => AudioManager.SetParameter(default, "Surface", (string)null));
        }

        [Test]
        public async Task SetParameter_ProviderThrows_IsIsolated()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));
            _fake.ThrowOnParameter = true;

            LogAssert.Expect(LogType.Error, new Regex("设置音频播放参数时 provider 抛出异常"));

            Assert.IsFalse(AudioManager.SetParameter(handle, "Health", 1f));
            Assert.IsTrue(AudioManager.IsPlaying(handle), "控制调用失败不得顺手回收 voice");
        }

        #endregion

        #region 全局参数

        [Test]
        public void SetGlobalParameter_ForwardsNumericAndLabel()
        {
            Assert.IsTrue(AudioManager.SetGlobalParameter("Weather", 0.5f));
            Assert.IsTrue(AudioManager.SetGlobalParameter("Weather", "Rain"));

            Assert.AreEqual(2, _fake.GlobalParameters.Count);
            Assert.AreEqual(0.5f, _fake.GlobalParameters[0].Value, 0.001f);
            Assert.AreEqual("Rain", _fake.GlobalParameters[1].Label);
        }

        [Test]
        public void SetGlobalParameter_RejectedByProvider_ReturnsFalse()
        {
            _fake.RejectGlobalParameter = true;

            Assert.IsFalse(AudioManager.SetGlobalParameter("Weather", 1f));
        }

        [Test]
        public void SetGlobalParameter_InvalidName_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => AudioManager.SetGlobalParameter(null, 1f));
            Assert.Throws<System.ArgumentException>(
                () => AudioManager.SetGlobalParameter("Weather", (string)null));
        }

        [Test]
        public void SetGlobalParameter_ProviderThrows_IsIsolated()
        {
            _fake.ThrowOnGlobalParameter = true;

            LogAssert.Expect(LogType.Error, new Regex("设置全局音频参数时 provider 抛出异常"));

            Assert.IsFalse(AudioManager.SetGlobalParameter("Weather", 1f));
        }

        #endregion

        #region 3D 位置

        [Test]
        public async Task SetPosition_ForwardsToProvider()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));
            var position = new Vector3(3f, 1f, -2f);

            Assert.IsTrue(AudioManager.SetPosition(handle, position));

            Assert.AreEqual(1, _fake.Positions.Count);
            Assert.AreEqual(position, _fake.Positions[0].Position);
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
        public async Task SetPosition_RejectedByProvider_ReturnsFalse()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));
            _fake.RejectPosition = true;

            Assert.IsFalse(AudioManager.SetPosition(handle, Vector3.one));
        }

        [Test]
        public async Task SetPosition_ProviderThrows_IsIsolated()
        {
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));
            _fake.ThrowOnPosition = true;

            LogAssert.Expect(LogType.Error, new Regex("更新音频 3D 位置时 provider 抛出异常"));

            Assert.IsFalse(AudioManager.SetPosition(handle, Vector3.one));
            Assert.IsTrue(AudioManager.IsPlaying(handle), "控制调用失败不得顺手回收 voice");
        }

        #endregion

        #region 未实现能力的 provider

        [Test]
        public async Task CapabilityMethods_WhenProviderDoesNotImplement_ReturnFalse()
        {
            var basic = new FakeAudioProvider();
            AudioManager.SetProvider(basic, new AudioInitOptions { MaxVoices = 4 });
            var handle = await AudioManager.PlayAsync("audio/a", new AudioPlayOptions(loop: true));

            Assert.IsFalse(AudioManager.SetParameter(handle, "Health", 1f));
            Assert.IsFalse(AudioManager.SetGlobalParameter("Weather", 1f));
            Assert.IsFalse(AudioManager.SetPosition(handle, Vector3.one));
        }

        #endregion
    }
}
