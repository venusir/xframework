using System.Text;
using UnityEngine;

namespace XFramework.XData
{
    /// <summary>
    /// 基于 Unity <see cref="JsonUtility"/> 的 JSON 存档存储。
    /// <para>零外部依赖，适合大多数中小体量游戏。</para>
    /// <para>注意：Unity JsonUtility 仅序列化 <c>[Serializable]</c> 的公共字段，
    /// 不支持 <c>Dictionary</c>、多态等高级特性。</para>
    /// </summary>
    public sealed class JsonFileDataStore : FileDataStore
    {
        /// <summary>
        /// 构造 JSON 文件存储。
        /// </summary>
        /// <param name="directory">存档子目录，默认 <c>"saves"</c>。</param>
        public JsonFileDataStore(string directory = "saves") : base(directory)
        {
        }

        #region Serialization

        /// <summary>
        /// 将 <see cref="SaveData"/> 序列化为 UTF-8 字节数组。
        /// <para><see cref="SaveData"/> 及其子类型均为 <c>[Serializable]</c>，可直接使用 <see cref="JsonUtility"/>。</para>
        /// </summary>
        protected override byte[] Serialize(SaveData data)
        {
            var json = JsonUtility.ToJson(data, prettyPrint: false);
            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// 将 UTF-8 字节数组反序列化为 <see cref="SaveData"/>。
        /// </summary>
        protected override SaveData Deserialize(byte[] bytes)
        {
            var json = Encoding.UTF8.GetString(bytes);
            return JsonUtility.FromJson<SaveData>(json);
        }

        #endregion
    }
}