using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUI.Data;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// ViewModel 释放路径的逐项隔离（R3-UIB-006）。
    /// <para>此前 <c>OnUnbound</c> 与 <c>Dispose</c> 同 try、<c>ViewModelBase.Dispose</c> 的两个循环也
    /// 无逐项隔离：一个属性/订阅抛异常会跳过其余项，全部泄漏。</para>
    /// </summary>
    [TestFixture]
    public class ViewModelBaseTests
    {
        private sealed class TestViewModel : ViewModelBase
        {
            public void Add(IDisposable disposable) => AddSubscription(disposable);
        }

        private sealed class ThrowingOnUnboundViewModel : ViewModelBase
        {
            public bool Disposed;

            public override void OnUnbound()
                => throw new InvalidOperationException("OnUnbound boom");

            public override void Dispose()
            {
                Disposed = true;
                base.Dispose();
            }
        }

        private sealed class ThrowingDisposable : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("dispose boom");
        }

        private sealed class CountingDisposable : IDisposable
        {
            public int Count { get; private set; }
            public void Dispose() => Count++;
        }

        [Test]
        public void Dispose_OneSubscriptionThrows_OthersStillDisposed()
        {
            var vm = new TestViewModel();
            var counting = new CountingDisposable();
            vm.Add(new ThrowingDisposable());
            vm.Add(counting);

            LogAssert.Expect(LogType.Error, new Regex("释放 ViewModel 订阅时抛出异常"));

            Assert.DoesNotThrow(() => vm.Dispose());
            Assert.AreEqual(1, counting.Count,
                "一个订阅释放失败不得跳过后续订阅（R3-UIB-006）");
        }

        [Test]
        public void Unbind_OnUnboundThrows_ViewModelStillDisposed()
        {
            var go = new GameObject("Binding", typeof(RectTransform));
            try
            {
                var binding = go.AddComponent<UIPanelBinding>();
                var vm = new ThrowingOnUnboundViewModel();
                binding.Bind(vm);

                LogAssert.Expect(LogType.Error, new Regex("ViewModel.OnUnbound 抛出异常"));

                binding.Unbind();

                Assert.IsTrue(vm.Disposed, "OnUnbound 抛异常不得跳过 Dispose（R3-UIB-006）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
