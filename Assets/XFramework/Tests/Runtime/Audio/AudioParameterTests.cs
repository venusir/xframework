using System;
using NUnit.Framework;

namespace XFramework.XAudio.Tests
{
    /// <summary>
    /// <see cref="AudioParameter"/> 的值语义与构造校验。
    /// </summary>
    [TestFixture]
    public class AudioParameterTests
    {
        [Test]
        public void FloatConstructor_CreatesNumericParameter()
        {
            var parameter = new AudioParameter("Health", 0.75f);

            Assert.AreEqual("Health", parameter.Name);
            Assert.AreEqual(0.75f, parameter.Value, 0.001f);
            Assert.IsNull(parameter.Label);
            Assert.IsFalse(parameter.IsLabel);
        }

        [Test]
        public void LabelConstructor_CreatesLabelParameter()
        {
            var parameter = new AudioParameter("Surface", "Grass");

            Assert.AreEqual("Surface", parameter.Name);
            Assert.AreEqual(0f, parameter.Value, 0.001f);
            Assert.AreEqual("Grass", parameter.Label);
            Assert.IsTrue(parameter.IsLabel);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void NumericConstructor_RejectsInvalidName(string name)
        {
            Assert.Throws<ArgumentException>(() => new AudioParameter(name, 1f));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void LabelConstructor_RejectsInvalidName(string name)
        {
            Assert.Throws<ArgumentException>(() => new AudioParameter(name, "Grass"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void LabelConstructor_RejectsInvalidLabel(string label)
        {
            Assert.Throws<ArgumentException>(() => new AudioParameter("Surface", label));
        }

        /// <summary>
        /// <c>default(AudioParameter)</c> 绕过构造函数校验，Name 为 null；核心的播放入口必须把它拦住。
        /// </summary>
        [Test]
        public void DefaultStruct_IsInvalid()
        {
            var parameter = default(AudioParameter);

            Assert.IsNull(parameter.Name);
            Assert.IsNull(parameter.Label);
            Assert.IsFalse(parameter.IsLabel);
        }
    }
}
