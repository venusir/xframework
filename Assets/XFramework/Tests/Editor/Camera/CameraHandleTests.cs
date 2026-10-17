using NUnit.Framework;

namespace XFramework.XCamera.Tests
{
    /// <summary>
    /// <see cref="CameraHandle"/> 的值语义：<c>default</c> 无效、相等性、便捷方法的默认短路。
    /// <para>纯结构测试：不初始化门面；<c>default</c> 句柄的便捷方法必须在门面之前短路。</para>
    /// </summary>
    [TestFixture]
    public class CameraHandleTests
    {
        [Test]
        public void Default_IsDefaultAndInvalid()
        {
            var handle = default(CameraHandle);

            Assert.IsTrue(handle.IsDefault);
            Assert.AreEqual(0, handle.Generation);
        }

        [Test]
        public void Constructor_NonZeroGeneration_IsNotDefault()
        {
            var handle = new CameraHandle(0, 1);

            Assert.IsFalse(handle.IsDefault);
            Assert.AreEqual(0, handle.Id);
            Assert.AreEqual(1, handle.Generation);
        }

        [Test]
        public void Equality_ComparesIdAndGeneration()
        {
            var left = new CameraHandle(3, 2);
            var right = new CameraHandle(3, 2);

            Assert.IsTrue(left == right);
            Assert.IsFalse(left != right);
            Assert.IsTrue(left.Equals(right));
            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        }

        [Test]
        public void Equality_DifferentIdOrGeneration_IsNotEqual()
        {
            var left = new CameraHandle(3, 2);

            Assert.IsFalse(left == new CameraHandle(4, 2));
            Assert.IsFalse(left == new CameraHandle(3, 3));
        }

        [Test]
        public void DefaultConvenienceMethods_ShortCircuitBeforeFacade()
        {
            var handle = default(CameraHandle);

            Assert.DoesNotThrow(() => handle.Activate());
            Assert.DoesNotThrow(() => handle.Deactivate());
            Assert.DoesNotThrow(() => handle.TransitionTo(1f));
            Assert.IsFalse(handle.IsActive());
            Assert.IsFalse(handle.Unregister());
            Assert.DoesNotThrow(() => handle.Dispose());
        }

        [Test]
        public void ToString_ContainsIdAndGeneration()
        {
            var text = new CameraHandle(3, 2).ToString();

            StringAssert.Contains("3", text);
            StringAssert.Contains("2", text);
        }
    }
}
