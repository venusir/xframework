using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 移动平台（iOS/Android）文件提供者实现。
    /// <para><see cref="FileDomain.Streaming"/> 域在移动端需通过 <see cref="UnityWebRequest"/> 读取，
    /// 因为 StreamingAssets 在 APK/IPA 内是压缩存储，无法用 <see cref="System.IO"/> 直接访问。</para>
    /// <para>其他域（<see cref="FileDomain.AppData"/>、<see cref="FileDomain.Cache"/>、<see cref="FileDomain.SaveData"/>）
    /// 仍使用 <see cref="System.IO"/>（这些路径在移动端沙盒内，IO 可用）。</para>
    /// </summary>
    public class MobileFileProvider : IFileProvider
    {
        #region Private Fields

        /// <summary>桌面平台的回退 Provider，用于非 Streaming 域的操作。</summary>
        private readonly DesktopFileProvider _desktopProvider = new DesktopFileProvider();

        #endregion

        #region IFileProvider

        /// <inheritdoc />
        public bool Exists(FileDomain domain, string relativePath)
        {
            if (domain == FileDomain.Streaming)
                return CheckStreamingExists(relativePath);

            return _desktopProvider.Exists(domain, relativePath);
        }

        /// <inheritdoc />
        public async UniTask<bool> ExistsAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            if (domain == FileDomain.Streaming)
                return await CheckStreamingExistsAsync(relativePath, cancellationToken);

            return await _desktopProvider.ExistsAsync(domain, relativePath, cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            if (domain == FileDomain.Streaming)
                return await ReadStreamingText(relativePath, cancellationToken);

            return await _desktopProvider.ReadAllTextAsync(domain, relativePath, cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            if (domain == FileDomain.Streaming)
                return await ReadStreamingBytes(relativePath, cancellationToken);

            return await _desktopProvider.ReadAllBytesAsync(domain, relativePath, cancellationToken);
        }

        /// <inheritdoc />
        public UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default)
        {
            // Streaming 域只读，写入走 DesktopProvider（它会定位到 AppData/Cache 等可写域）
            return _desktopProvider.WriteAllTextAsync(domain, relativePath, content, cancellationToken);
        }

        /// <inheritdoc />
        public UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            // Streaming 域只读，写入走 DesktopProvider
            return _desktopProvider.WriteAllBytesAsync(domain, relativePath, data, cancellationToken);
        }

        /// <inheritdoc />
        public void Delete(FileDomain domain, string relativePath)
        {
            _desktopProvider.Delete(domain, relativePath);
        }

        /// <inheritdoc />
        public UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default)
        {
            // Streaming 域暂不支持枚举文件（移动平台限制），返回空数组
            if (domain == FileDomain.Streaming)
                return UniTask.FromResult(Array.Empty<string>());

            return _desktopProvider.GetFilesAsync(domain, relativePath, searchPattern, cancellationToken);
        }

        /// <inheritdoc />
        public void CreateDirectory(FileDomain domain, string relativePath)
        {
            _desktopProvider.CreateDirectory(domain, relativePath);
        }

        /// <inheritdoc />
        public string GetPhysicalPath(FileDomain domain, string relativePath)
        {
            return _desktopProvider.GetPhysicalPath(domain, relativePath);
        }

        #endregion

        #region Private Methods — Streaming 域

        /// <summary>
        /// 检查 StreamingAssets 中的文件是否存在（通过 UnityWebRequest HEAD 请求）。
        /// <para>同步自旋等待（仅在同步 <see cref="Exists"/> 中调用）：
        /// isDone 由引擎原生侧推进，不依赖托管 PlayerLoop，故阻塞等待可完成。</para>
        /// </summary>
        private static bool CheckStreamingExists(string relativePath)
        {
            var url = GetStreamingUrl(relativePath);
            using var request = UnityWebRequest.Head(url);
            request.SendWebRequest();

            // 同步等待（仅在 Exists 中调用，频率低）
            while (!request.isDone) { }

            return request.result == UnityWebRequest.Result.Success;
        }

        /// <summary>
        /// 异步检查 StreamingAssets 中的文件是否存在（通过 UnityWebRequest HEAD 请求）。
        /// </summary>
        private static async UniTask<bool> CheckStreamingExistsAsync(string relativePath, CancellationToken cancellationToken)
        {
            var url = GetStreamingUrl(relativePath);
            using var request = UnityWebRequest.Head(url);

            var asyncOp = request.SendWebRequest();
            // cancelImmediately: 取消时立即 Abort 请求（对齐原实现的手动 Abort 语义）
            await asyncOp.ToUniTask(cancellationToken: cancellationToken, cancelImmediately: true);

            return request.result == UnityWebRequest.Result.Success;
        }

        /// <summary>
        /// 通过 UnityWebRequest 读取 StreamingAssets 中的文本文件。
        /// </summary>
        private static async UniTask<string> ReadStreamingText(string relativePath, CancellationToken cancellationToken)
        {
            var url = GetStreamingUrl(relativePath);

            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();

            var asyncOp = request.SendWebRequest();
            // cancelImmediately: 取消时立即 Abort 请求（对齐原实现的手动 Abort 语义）
            await asyncOp.ToUniTask(cancellationToken: cancellationToken, cancelImmediately: true);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"MobileFileProvider: failed to read Streaming text '{relativePath}': {request.error}");
                return null;
            }

            return request.downloadHandler.text;
        }

        /// <summary>
        /// 通过 UnityWebRequest 读取 StreamingAssets 中的二进制文件。
        /// </summary>
        private static async UniTask<byte[]> ReadStreamingBytes(string relativePath, CancellationToken cancellationToken)
        {
            var url = GetStreamingUrl(relativePath);

            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();

            var asyncOp = request.SendWebRequest();
            // cancelImmediately: 取消时立即 Abort 请求（对齐原实现的手动 Abort 语义）
            await asyncOp.ToUniTask(cancellationToken: cancellationToken, cancelImmediately: true);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"MobileFileProvider: failed to read Streaming bytes '{relativePath}': {request.error}");
                return null;
            }

            return request.downloadHandler.data;
        }

        /// <summary>
        /// 构建 StreamingAssets 完整 URL。Android 上自动添加 <c>jar:file://</c> 前缀。
        /// </summary>
        private static string GetStreamingUrl(string relativePath)
        {
            // 确保路径以 / 开头
            var path = relativePath;
            if (!path.StartsWith("/"))
                path = "/" + path;

            return Application.streamingAssetsPath + path;
        }

        #endregion
    }
}