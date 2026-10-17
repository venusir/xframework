using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using XFramework.XAsset;

namespace XFramework.XAsset.Tests
{
    /// <summary>
    /// 资产加载取消善后（AST-001）：等待被取消时必须释放已创建的句柄。
    /// <para><b>红证在缝上做</b>——真实 YooAsset 句柄的构造是 internal，替身造不出来；
    /// 缝之上是「取消 → Release → 重抛」，缝之下（<c>HandleBaseWait</c>）只是一层机械转发。
    /// 真实 YooAsset 包端到端仍属未覆盖项（审计报告 §7 Asset 残留）。</para>
    /// </summary>
    [TestFixture]
    public class AssetLoadWaitTests
    {
        private sealed class FakeLoadWait : IAssetLoadWait
        {
            public bool Done;
            public int ReleaseCount;

            public bool IsDone => Done;

            public void Release() => ReleaseCount++;
        }

        [Test]
        public async Task WaitOrReleaseAsync_Cancelled_ReleasesHandleAndRethrows()
        {
            var wait = new FakeLoadWait { Done = false };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                await YooAssetManagerImpl.WaitOrReleaseAsync(wait, cts.Token);
                Assert.Fail("取消应重抛 OperationCanceledException");
            }
            catch (System.OperationCanceledException)
            {
            }
            finally
            {
                cts.Dispose();
            }

            Assert.AreEqual(1, wait.ReleaseCount, "取消后必须释放已 RefCount++ 的句柄（AST-001）");
        }

        [Test]
        public async Task WaitOrReleaseAsync_Completed_DoesNotRelease()
        {
            var wait = new FakeLoadWait { Done = true };

            await YooAssetManagerImpl.WaitOrReleaseAsync(wait, CancellationToken.None);

            Assert.AreEqual(0, wait.ReleaseCount, "正常完成不释放——句柄要交给调用方");
        }
    }
}
