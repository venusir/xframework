using System;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace XFramework.XFileManager.Tests
{
    /// <summary>
    /// 真实 <see cref="DesktopFileProvider"/> 的线程契约测试。
    /// <para>本模块其余 fixture 都注入 <c>TempFileProvider</c>——它在构造函数里就把域根定死，
    /// 结构性绕开了 Unity API，因此「域根解析是否碰了 Unity API」这一整类缺陷在替身上不可见。
    /// 本 fixture 刻意走零配置的真实 Provider，锁住两件事：<b>目标目录存在时</b>枚举不抛；
    /// 以及<b>从线程池线程调用</b>同样不抛。</para>
    /// <para>用 <see cref="FileDomain.SaveData"/> 域（桌面即 <c>persistentDataPath</c>），与
    /// <c>SaveManagerImpl.RecoverAsync</c> 里唯一不切回主线程的那条路径同域；只读枚举，不写任何文件。</para>
    /// </summary>
    [TestFixture]
    public class DesktopFileProviderThreadTests
    {
        /// <summary>
        /// 零配置桌面实现的域根。走 Provider 自己的映射而不是在测试里硬写
        /// <c>Application.persistentDataPath</c>，避免第二份真相。
        /// </summary>
        private static string DomainRoot => new DesktopFileProvider().GetPhysicalPath(FileDomain.SaveData, null);

        private int _mainThreadId;

        [SetUp]
        public void SetUp()
        {
            _mainThreadId = Environment.CurrentManagedThreadId;

            // 零配置初始化：编辑器下即 DesktopFileProvider（生产路径同款）
            FileManager.Destroy();
            FileManager.Initialize();

            // 目录必须存在，否则 GetFilesAsync 会走「目录不存在即返回空」的提前返回，什么都锁不住
            Directory.CreateDirectory(DomainRoot);
        }

        [TearDown]
        public void TearDown()
        {
            FileManager.Destroy();
            FileManager.Initialize();
        }

        [Test]
        public async Task GetFilesAsync_ExistingDirectory_DoesNotThrow_OnMainThread()
        {
            var files = await FileManager.GetFilesAsync(FileDomain.SaveData, "");

            Assert.IsNotNull(files);
        }

        [Test]
        public async Task GetDirectoriesAsync_ExistingDirectory_DoesNotThrow_OnMainThread()
        {
            var directories = await FileManager.GetDirectoriesAsync(FileDomain.SaveData, "");

            Assert.IsNotNull(directories);
        }

        /// <summary>
        /// 缺陷回归锁：Provider 的 IO 在 <c>RunOnThreadPool(configureAwait: false)</c> 上完成后会把
        /// 池线程留给调用方续体，而 <c>SaveManagerImpl.RecoverAsync</c> 的契约正是「全程不切回主线程」
        /// ——因此域根解析必须与线程无关。此前它在这里读 <c>Application.persistentDataPath</c>，
        /// 于是每次启动的恢复扫描必崩（EditMode 的 SaveBootstrapStage 用例独红了 7 次）。
        /// </summary>
        [Test]
        public async Task GetFilesAsync_FromThreadPool_DoesNotThrow()
        {
            await UniTask.SwitchToThreadPool();
            var callThreadId = Environment.CurrentManagedThreadId;

            string[] files;
            try
            {
                files = await FileManager.GetFilesAsync(FileDomain.SaveData, "");
            }
            finally
            {
                await UniTask.SwitchToMainThread();
            }

            Assert.AreNotEqual(_mainThreadId, callThreadId,
                "前提：调用必须真的落在池线程上，否则本用例锁不住东西");
            Assert.IsNotNull(files);
        }

        [Test]
        public async Task GetDirectoriesAsync_FromThreadPool_DoesNotThrow()
        {
            await UniTask.SwitchToThreadPool();
            var callThreadId = Environment.CurrentManagedThreadId;

            string[] directories;
            try
            {
                directories = await FileManager.GetDirectoriesAsync(FileDomain.SaveData, "");
            }
            finally
            {
                await UniTask.SwitchToMainThread();
            }

            Assert.AreNotEqual(_mainThreadId, callThreadId,
                "前提：调用必须真的落在池线程上，否则本用例锁不住东西");
            Assert.IsNotNull(directories);
        }
    }
}
