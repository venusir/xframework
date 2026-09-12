using System;
using System.IO;
using UnityEngine;
using XFramework.XFileManager;

namespace XFramework.XSettings
{
    /// <summary>
    /// 基于 JSON 文件的设置存储后端。
    /// <para>使用 Unity 内置 <see cref="JsonUtility"/> 进行序列化/反序列化，无需额外依赖。</para>
    /// <para>设置类型 T 必须标记 <see cref="SerializableAttribute"/>。</para>
    /// <para><b>落盘布局：</b>正式文件 <c>settings.json</c>、写入中的临时文件 <c>.tmp</c>、
    /// 一代备份 <c>.bak</c>（后缀取自 <see cref="FilePathUtility"/>）。写入走
    /// <see cref="FilePathUtility.ReplaceFileAtomically"/>，保证任意时刻正式文件与备份
    /// 至少有一个完整存在，写入中途崩溃不会留下半截 JSON。</para>
    /// <para><b>失败语义：</b>读取或解析失败一律 LogWarning 并回退，不向调用方抛异常。
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
            // 主文件不存在 = 无持久化数据,交给上层用 defaultFactory。
            // 这里必须与「存在但损坏」分开:若缺失也回退备份,Reset(删文件)之后的下一次 Load
            // 会把玩家刚重置掉的旧数据从 .bak 里恢复回来
            if (!File.Exists(_filePath))
                return new T();

            if (TryLoadFrom(_filePath, out T loaded))
                return loaded;

            var backupPath = _filePath + FilePathUtility.BackupFileSuffix;
            if (TryLoadFrom(backupPath, out loaded))
            {
                Debug.LogWarning($"[SettingsManager] 主设置文件不可用，已从备份恢复：'{backupPath}'");
                return loaded;
            }

            return new T();
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> 为 <c>null</c> 时抛出。</exception>
        public void Save<T>(T settings) where T : class, new()
        {
            // 原先静默 return:与 Apply(null) 抛异常不一致,且会让「保存没生效」变得极难排查。
            // 注意这里抛的是参数错误(调用方的 bug),与下面 IO 失败只告警是两回事
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonUtility.ToJson(settings, true);

            // 原子写:先落 .tmp,再整份替换正式文件,旧内容留作 .bak。
            // 直接 WriteAllText 覆盖正式文件的话,写到一半崩溃就留下截断的 JSON
            var tempPath = _filePath + FilePathUtility.TempFileSuffix;
            var backupPath = _filePath + FilePathUtility.BackupFileSuffix;

            try
            {
                File.WriteAllText(tempPath, json);
                FilePathUtility.ReplaceFileAtomically(tempPath, _filePath, backupPath);
            }
            catch (Exception ex)
            {
                // IO 失败(磁盘满/权限/占用)只告警不抛:存档失败不该让游戏崩掉。
                // 参数错误与 IO 失败的区别正在于此——前者是调用方的 bug,后者是环境问题
                TryDelete(tempPath);
                Debug.LogWarning(
                    $"[SettingsManager] 写入设置文件失败：'{_filePath}'（{ex.GetType().Name}: {ex.Message}）");
            }
        }

        /// <inheritdoc />
        /// <remarks>同时清除 <c>.tmp</c> 与 <c>.bak</c>，使「重置」不留任何可被后续 Load 恢复的残留。</remarks>
        public void Delete()
        {
            TryDelete(_filePath);
            TryDelete(_filePath + FilePathUtility.TempFileSuffix);
            TryDelete(_filePath + FilePathUtility.BackupFileSuffix);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 尝试从指定文件读取并解析设置。
        /// <para>文件不存在或内容为空返回 <c>false</c> 且不告警（属正常情况）；
        /// 读失败或解析失败返回 <c>false</c> 并告警。</para>
        /// </summary>
        private static bool TryLoadFrom<T>(string path, out T loaded) where T : class, new()
        {
            loaded = null;

            if (!File.Exists(path))
                return false;

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                // 读失败(占用/权限/IO)与内容损坏同等对待:玩家至多丢设置,不该开不了游戏
                Debug.LogWarning(
                    $"[SettingsManager] 读取设置文件失败：'{path}'（{ex.GetType().Name}: {ex.Message}）");
                return false;
            }

            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                loaded = JsonUtility.FromJson<T>(json) ?? new T();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[SettingsManager] 设置文件内容无法解析为 {typeof(T).Name}：'{path}'（{ex.Message}）");
                return false;
            }
        }

        /// <summary>
        /// 尽力删除文件：不存在或删除失败都不抛异常。
        /// <para>用于清理路径与失败回滚——清理本身失败不应盖过原始错误。</para>
        /// </summary>
        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SettingsManager] 删除文件失败：'{path}'（{ex.GetType().Name}: {ex.Message}）");
            }
        }

        #endregion
    }
}
