using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using XFramework.XSave.Tests;

namespace XFramework.XFileManager.Tests
{
    /// <summary>
    /// <see cref="FileManager.DeleteAsync"/> 的契约测试。
    /// <para><b>它锁的核心事实</b>：删除在<b>池线程</b>上执行——门面把 Provider 的同步
    /// <see cref="IFileProvider.Delete"/> 放进 <c>UniTask.RunOnThreadPool</c> 调用（<c>IFileProvider</c>
    /// 没有异步删除孪生，而各平台的后端删除本质上都是同步原语）。Save 的四个删除 API 因此不再在主线程
    /// 做 1~4×N 次同步 IO。</para>
    /// <para><b>对照组 <see cref="Delete_Sync_RunsOnCallingThread"/> 是刻意的</b>：同步 <c>Delete</c>
    /// 是直通原语、不得被「顺手」也包进线程池（那只是让调用方多付一次线程池往返、照样阻塞，还会悄悄改变
    /// Provider 的执行线程）；将来若有人那么改，它会红并逼出一次裁定。</para>
    /// </summary>
    [TestFixture]
    public class FileManagerDeleteTests
    {
        private TempFileProvider _temp;
        private ThreadRecordingFileProvider _recording;

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _temp?.Cleanup();
        }

        [SetUp]
        public void SetUp()
        {
            _temp?.Cleanup();
            _temp = new TempFileProvider();
            _recording = new ThreadRecordingFileProvider(_temp);
            ThreadRecordingFileProvider.Reset();

            FileManager.Destroy();
            FileManager.Initialize(_recording);
        }

        [TearDown]
        public void TearDown()
        {
            FileManager.SetCryptoProvider(null);
            FileManager.Destroy();

            // 还原成「零配置可用」——同 FileManagerDirectoryTests：懒加载豁免只对「未初始化」生效
            FileManager.Initialize();
        }

        [Test]
        public async Task DeleteAsync_RunsOffTheMainThread()
        {
            // 两条纪律（都由「反向注入」逼出来的，别改回去）：
            // ① 主线程号在任何 await 之前取；
            // ② 准备数据必须用**同步**写入——UniTask 不捕获 SynchronizationContext（它不是 Task），
            //    任何一次 await 之后的续体都可能落在池线程上，那时「当前线程」就不再是主线程，
            //    断言会失真（本用例第二版正是这么失真的：注入「异步外壳、同步执行」后它仍然绿）
            var mainThreadId = Environment.CurrentManagedThreadId;
            FileManager.WriteAllText(FileDomain.SaveData, "slot_1.save", "x");

            await FileManager.DeleteAsync(FileDomain.SaveData, "slot_1.save");

            Assert.AreEqual(1, ThreadRecordingFileProvider.DeleteCount, "前提：删除确实发生过");
            Assert.AreNotEqual(mainThreadId, ThreadRecordingFileProvider.LastDeleteThreadId,
                "DeleteAsync 应把 Provider 的同步 Delete 放进池线程执行——否则 Save 的批量删除会卡主线程");
        }

        [Test]
        public void Delete_Sync_RunsOnCallingThread()
        {
            // 对照组：同步面是直通原语（不是「异步面的阻塞包装」），执行线程就是调用线程
            var mainThreadId = Environment.CurrentManagedThreadId;
            FileManager.WriteAllText(FileDomain.SaveData, "slot_1.save", "x");
            ThreadRecordingFileProvider.Reset();

            FileManager.Delete(FileDomain.SaveData, "slot_1.save");

            Assert.AreEqual(1, ThreadRecordingFileProvider.DeleteCount, "前提：删除确实发生过");
            Assert.AreEqual(mainThreadId, ThreadRecordingFileProvider.LastDeleteThreadId,
                "同步 Delete 只在调用线程上执行——把它也包进线程池只会让调用方多付一次往返、照样阻塞");
        }

        [Test]
        public async Task DeleteAsync_MissingFile_IsNoOp()
        {
            Assert.DoesNotThrowAsync(async () =>
                await FileManager.DeleteAsync(FileDomain.SaveData, "never-existed.save"));

            Assert.IsFalse(await FileManager.ExistsAsync(FileDomain.SaveData, "never-existed.save"));
        }

        [Test]
        public async Task DeleteAsync_CancelledToken_ThrowsAndDoesNotDelete()
        {
            await FileManager.WriteAllTextAsync(FileDomain.SaveData, "slot_1.save", "x");
            ThreadRecordingFileProvider.Reset();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await FileManager.DeleteAsync(FileDomain.SaveData, "slot_1.save", cts.Token));

            Assert.AreEqual(0, ThreadRecordingFileProvider.DeleteCount, "已取消的令牌不得进入委托");
            Assert.IsTrue(await FileManager.ExistsAsync(FileDomain.SaveData, "slot_1.save"),
                "取消只保证「尚未开始的删除不会发生」——这一次还没开始");
        }

        [Test]
        public async Task DeleteAsync_CryptoEnabled_StillDeletes()
        {
            // 防装饰器遮蔽：开加密后门面走的是 CryptoFileProvider，它的 Delete 必须透传
            FileManager.SetCryptoProvider(new XorCryptoProvider("test-key"));
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "secret.save", Encoding.UTF8.GetBytes("payload"));
            Assert.IsTrue(await FileManager.ExistsAsync(FileDomain.SaveData, "secret.save"));

            await FileManager.DeleteAsync(FileDomain.SaveData, "secret.save");

            Assert.IsFalse(await FileManager.ExistsAsync(FileDomain.SaveData, "secret.save"), "加密域同样能删");
        }
    }
}
