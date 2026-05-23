using System.IO;
using UnityEngine;

namespace XFramework.XSettings
{
    /// <summary>
    /// 基于 JSON 文件的设置存储后端。
    /// <para>使用 Unity 内置 <see cref="JsonUtility"/> 进行序列化/反序列化，无需额外依赖。</para>
    /// <para>设置类型 T 必须标记 <see cref="System.SerializableAttribute"/>。</para>
    /// </summary>
    public class JsonFileStore : ISettingsStore
    {
        #region Private Fields

        private readonly string _filePath;

        #endregion

        #region Constructors

        /// <summary>
        /// 创建 JSON 文件存储后端。
        /// </summary>
        /// <param name="filePath">JSON 文件完整路径（含文件名）。例如 <c>Application.persistentDataPath + "/settings.json"</c>。</param>
        public JsonFileStore(string filePath)
        {
            _filePath = filePath;
        }

        #endregion

        #region ISettingsStore

        /// <inheritdoc />
        public bool Exists()
        {
            return File.Exists(_filePath);
        }

        /// <inheritdoc />
        public T Load<T>() where T : class, new()
        {
            if (!Exists())
                return new T();

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrEmpty(json))
                return new T();

            var result = JsonUtility.FromJson<T>(json);
            return result ?? new T();
        }

        /// <inheritdoc />
        public void Save<T>(T settings) where T : class, new()
        {
            if (settings == null)
                return;

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonUtility.ToJson(settings, true);
            File.WriteAllText(_filePath, json);
        }

        /// <inheritdoc />
        public void Delete()
        {
            if (Exists())
                File.Delete(_filePath);
        }

        #endregion
    }
}