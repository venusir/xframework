using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XAudio;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// <see cref="AudioManager.CopyChannels"/>：通道表投影。
    /// <para>本 fixture 只锁**契约面**（未初始化时返回 0 并清空缓冲区、参数防御）——通道表的内容
    /// 需要真实初始化与音频设备，那部分由 <c>AudioLifecycleTests</c> 一族覆盖。</para>
    /// </summary>
    [TestFixture]
    public class AudioDiagnosticsTests
    {
        [SetUp]
        public void SetUp()
        {
            AudioManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            AudioManager.Destroy();
        }

        [Test]
        public void CopyChannels_NotInitialized_ReturnsZeroAndClearsBuffer()
        {
            var buffer = new List<AudioChannelInfo> { default };

            int count = AudioManager.CopyChannels(buffer);

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void CopyChannels_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AudioManager.CopyChannels(null));
        }
    }
}
