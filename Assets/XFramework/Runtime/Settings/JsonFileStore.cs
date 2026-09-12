using System;
using System.IO;
using UnityEngine;

namespace XFramework.XSettings
{
    /// <summary>
    /// 基于 JSON 文件的设置存储后端。
    /// <para>使用 Unity 内置 <see cref="JsonUtility"/> 进行序列化/反序列化，无需额外依赖。</para>
    /// <para>设置类型 T 必须标记 <see cref="SerializableAttribute"/>。</para>
    /// <para><b>失败语义：</b>读取或解析失败一律 LogWarning 并回退默认值，不向调用方抛异常。
    /// 配置文件损坏不应让游戏启动失败——若向上抛，玩家此后每次启动都会崩且无法自救
    /// （设置文件由游戏自己写，玩家通常也不知道该删哪个文件）。</para>
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
        /// <exception cref="ArgumentException"><paramref name="filePath"/> 为 <c>null</c>、空或全空白时抛出。</exception>
        public JsonFileStore(string filePath)
        {
            // 早失败:空路径会让后续每次 IO 都抛,错误点离病因很远
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException(
                    "[SettingsManager] JsonFileStore 的 filePath 不能为空或全空白。", nameof(filePath));

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

            string json;
            try
            {
                json = File.ReadAllText(_filePath);
            }
            catch (Exception ex)
            {
                // 读失败(占用/权限/IO)与内容损坏同等对待:玩家至多丢设置,不该开不了游戏
                Debug.LogWarning(
                    $"[SettingsManager] 读取设置文件失败，已回退默认值：'{_filePath}'" +
                    $"（{ex.GetType().Name}: {ex.Message}）");
                return new T();
            }

            if (string.IsNullOrEmpty(json))
                return new T();

            try
            {
                return JsonUtility.FromJson<T>(json) ?? new T();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[SettingsManager] 设置文件内容无法解析为 {typeof(T).Name}，已回退默认值：'{_filePath}'" +
                    $"（{ex.Message}）");
                return new T();
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> 为 <c>null</c> 时抛出。</exception>
        public void Save<T>(T settings) where T : class, new()
        {
            // 原先静默 return:与 Apply(null) 抛异常不一致,且会让「保存没生效」变得极难排查
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

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
