using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 桌面平台（Windows/Linux/macOS Standalone）文件提供者实现。
    /// <para>直接使用 <see cref="System.IO"/> API，性能最优。</para>
    /// <para>实现 <see cref="IAtomicFileProvider"/>：原子写为「写 .tmp 临时文件 → 一步替换正式文件并保留
    /// 一代 .bak 备份」，写入中途崩溃不会损坏已有文件，也不会出现「零副本」窗口。</para>
    /// <para>同时实现 <see cref="IDirectoryProvider"/>，提供直接子目录枚举（非递归）。</para>
    /// </summary>
    public class DesktopFileProvider : IFileProvider, IAtomicFileProvider, IDirectoryProvider
    {
        #region Domain Roots

        /// <summary>
        /// 域根缓存。Unity 的路径属性（<see cref="Application.persistentDataPath"/> 等）只能在主线程读取，
        /// 而本类的 IO 在线程池上执行、调用方续体也可能停留在池线程（<c>SaveManagerImpl.RecoverAsync</c>
        /// 的线程契约就是「全程不切回主线程」）。故域根在<b>主线程解析一次后缓存</b>，此后
        /// <see cref="GetPhysicalPath"/> 退化为纯字符串运算，可从任意线程调用。
        /// </summary>
        private static string _appDataRoot;
        private static string _streamingRoot;
        private static string _cacheRoot;

        /// <summary>
        /// 在主线程解析并缓存三个域根。幂等；由 <see cref="FileManager.Initialize"/> 与下面的加载钩子调用。
        /// <para>解析结果为空时<b>不缓存</b>——钩子若早于 Unity 路径就绪，留待下一次（主线程的初始化）
        /// 再解析，免得把空字符串固化成域根、让相对路径落到进程工作目录上。</para>
        /// </summary>
        internal static void PrimeRoots()
        {
            if (_appDataRoot != null)
                return;

            var appData = Application.persistentDataPath;
            var streaming = Application.streamingAssetsPath;
            var cache = Application.temporaryCachePath;
            if (string.IsNullOrEmpty(appData) || string.IsNullOrEmpty(streaming) || string.IsNullOrEmpty(cache))
                return;

            _appDataRoot = appData;
            _streamingRoot = streaming;
            _cacheRoot = cache;
        }

        /// <summary>
        /// 编辑器加载（含重编译引发的域重载）与进入播放时预热域根缓存。
        /// <para><b>两个特性都要挂：</b>编辑器里 <c>InitializeOnLoadMethod</c> 只在程序集加载时执行，
        /// 而在 Project Settings → Editor 里关闭 Reload Domain 后，进入播放不会重新加载程序集、该回调
        /// 不再执行；<c>RuntimeInitializeOnLoadMethod</c> 在进入播放时同样会执行，是那种情况下唯一的
        /// 时机（与 <c>UpdateManager.AutoInit</c> 同款理由）。</para>
        /// <para><b>只预热、不初始化门面</b>——FileManager 的零配置懒初始化是有意设计（见模块 README）。
        /// 预热的价值在于：进程内第一次文件调用即便来自子线程，域根也已在主线程取好。</para>
        /// </summary>
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void PrimeRootsOnLoad()
        {
            PrimeRoots();
        }

        #endregion

        #region IFileProvider

        /// <inheritdoc />
        public bool Exists(FileDomain domain, string relativePath)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            return File.Exists(fullPath);
        }

        /// <inheritdoc />
        public UniTask<bool> ExistsAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            return UniTask.RunOnThreadPool(
                () => File.Exists(fullPath),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            if (!File.Exists(fullPath))
                return null;

            return await UniTask.RunOnThreadPool(
                () => File.ReadAllText(fullPath),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            if (!File.Exists(fullPath))
                return null;

            return await UniTask.RunOnThreadPool(
                () => File.ReadAllBytes(fullPath),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            EnsureDirectoryExists(fullPath);

            await UniTask.RunOnThreadPool(
                () => File.WriteAllText(fullPath, content ?? string.Empty),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            EnsureDirectoryExists(fullPath);

            await UniTask.RunOnThreadPool(
                () => File.WriteAllBytes(fullPath, data ?? Array.Empty<byte>()),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public void Delete(FileDomain domain, string relativePath)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        /// <inheritdoc />
        public async UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            // 相对路径转换要用域根，故一并提到委托外：委托跑在池线程上，而域根的解析碰 Unity API
            // （缓存命中后是纯字符串运算，但取值这一步不该留在池线程里）
            var rootDir = GetDomainRoot(domain);

            return await UniTask.RunOnThreadPool(
                () =>
                {
                    if (!Directory.Exists(fullPath))
                        return Array.Empty<string>();

                    var files = Directory.GetFiles(fullPath, searchPattern);

                    // 转换为相对路径（统一正斜杠分隔）
                    for (int i = 0; i < files.Length; i++)
                    {
                        files[i] = FilePathUtility.ToRelativePath(rootDir, files[i]);
                    }

                    return files;
                },
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public void CreateDirectory(FileDomain domain, string relativePath)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }

        /// <inheritdoc />
        public async UniTask<string[]> GetDirectoriesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            // 与 GetFilesAsync 同理：域根在委托外取好，别把 Unity API 留在池线程上
            var rootDir = GetDomainRoot(domain);

            return await UniTask.RunOnThreadPool(
                () =>
                {
                    if (!Directory.Exists(fullPath))
                        return Array.Empty<string>();

                    // 非递归:仅直接子目录,与 GetFilesAsync 的粒度一致
                    var dirs = Directory.GetDirectories(fullPath);

                    // 转换为相对路径（统一正斜杠分隔，与 GetFilesAsync 同契约）
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        dirs[i] = FilePathUtility.ToRelativePath(rootDir, dirs[i]);
                    }

                    return dirs;
                },
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public string GetPhysicalPath(FileDomain domain, string relativePath)
        {
            var root = GetDomainRoot(domain);
            if (string.IsNullOrEmpty(relativePath))
                return root;

            // 路径沙箱收敛点：所有域的相对路径在此统一校验，拒绝 .. 穿越、盘符与 UNC，
            // 防止第三方或业务层注入非法路径读写到域根之外（门面不接触物理路径，必须在此拦截）
            if (!FilePathUtility.TryNormalizeRelativePath(relativePath, out var normalized))
                throw new ArgumentException(
                    $"[FileManager] 非法相对路径 '{relativePath}':不允许盘符、UNC 或 '..' 段穿越。",
                    nameof(relativePath));

            return Path.Combine(root, normalized);
        }

        #endregion

        #region IAtomicFileProvider

        /// <inheritdoc />
        public async UniTask WriteAllBytesAtomicAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            var tempPath = relativePath + FilePathUtility.TempFileSuffix;
            var backupPath = relativePath + FilePathUtility.BackupFileSuffix;
            var srcPhysical = GetPhysicalPath(domain, tempPath);
            var dstPhysical = GetPhysicalPath(domain, relativePath);
            var bakPhysical = GetPhysicalPath(domain, backupPath);

            // 先写 .tmp 临时文件：写入失败时正式文件保持完整
            await WriteAllBytesAsync(domain, tempPath, data, cancellationToken);

            // 替换流程为同步 IO，移出主线程。不再用「删正式 → Move」(Unity API 面无 File.Move(overwrite)
            // 重载)，改为一步替换并保留一代备份——原写法在删除与重命名之间崩溃会让存档彻底消失，
            // 详见 FilePathUtility.ReplaceFileAtomically 的不变式说明
            await UniTask.RunOnThreadPool(
                () => FilePathUtility.ReplaceFileAtomically(srcPhysical, dstPhysical, bakPhysical),
                configureAwait: false,
                cancellationToken);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 获取域对应的物理根目录。优先读缓存（见 <see cref="PrimeRoots"/>），未预热时才在当前线程
        /// 现取一次——因此<b>首次</b>调用须在主线程，而 <see cref="FileManager.Initialize"/> 已把这次
        /// 解析安排在初始化时完成。
        /// </summary>
        private static string GetDomainRoot(FileDomain domain)
        {
            if (_appDataRoot == null)
                PrimeRoots();

            switch (domain)
            {
                case FileDomain.AppData:
                case FileDomain.SaveData:
                    // 桌面平台 SaveData 等同于 AppData
                    return _appDataRoot ?? Application.persistentDataPath;
                case FileDomain.Streaming:
                    return _streamingRoot ?? Application.streamingAssetsPath;
                case FileDomain.Cache:
                    return _cacheRoot ?? Application.temporaryCachePath;
                default:
                    throw new ArgumentOutOfRangeException(nameof(domain), domain, null);
            }
        }

        /// <summary>
        /// 确保文件所在目录存在。
        /// </summary>
        private static void EnsureDirectoryExists(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        #endregion
    }
}