using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XFileManager;

namespace XFramework.XData
{
    /// <summary>
    /// 基于 <see cref="FileManager"/> 的文件存读抽象基类。
    /// <para>子类只需实现 <see cref="Serialize"/> 和 <see cref="Deserialize"/>，
    /// 文件路径、目录创建、Exists/Delete 都由基类处理。</para>
    /// <para>适用场景：本地文件存档（JSON / Protobuf / MessagePack 等）。</para>
    /// </summary>
    public abstract class FileDataStore : IDataStore
    {
        private readonly string _directory;

        /// <summary>
        /// 构造文件存储。
        /// </summary>
        /// <param name="directory">存档子目录，默认 <c>"saves"</c>。</param>
        protected FileDataStore(string directory = "saves")
        {
            _directory = directory;
        }

        #region IDataStore

        /// <inheritdoc/>
        public virtual async UniTask SaveAsync(string name, SaveData data, CancellationToken ct = default)
        {
            var filePath = $"{_directory}/{name}";
            FileManager.CreateDirectory(FileDomain.AppData, _directory);
            var bytes = Serialize(data);
            await FileManager.WriteAllBytesAsync(FileDomain.AppData, filePath, bytes, ct);
        }

        /// <inheritdoc/>
        public virtual async UniTask<SaveData> LoadAsync(string name, CancellationToken ct = default)
        {
            var filePath = $"{_directory}/{name}";
            var bytes = await FileManager.ReadAllBytesAsync(FileDomain.AppData, filePath, ct);
            if (bytes == null)
                return null;
            return Deserialize(bytes);
        }

        /// <inheritdoc/>
        public virtual void Delete(string name)
        {
            var filePath = $"{_directory}/{name}";
            FileManager.Delete(FileDomain.AppData, filePath);
        }

        /// <inheritdoc/>
        public virtual bool Exists(string name)
        {
            var filePath = $"{_directory}/{name}";
            return FileManager.Exists(FileDomain.AppData, filePath);
        }

        #endregion

        #region Abstract

        /// <summary>
        /// 将 <see cref="SaveData"/> 序列化为字节数组。
        /// </summary>
        protected abstract byte[] Serialize(SaveData data);

        /// <summary>
        /// 将字节数组反序列化为 <see cref="SaveData"/>。
        /// </summary>
        protected abstract SaveData Deserialize(byte[] bytes);

        #endregion
    }
}