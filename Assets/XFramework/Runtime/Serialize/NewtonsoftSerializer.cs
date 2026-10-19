using System;
using System.Text;
using Newtonsoft.Json;

namespace XFramework.XSerialize
{
    /// <summary>
    /// 基于 Newtonsoft.Json 的 JSON 序列化器，框架默认实现。
    /// <para>相比 <see cref="JsonSerializer"/>（JsonUtility）支持 Dictionary 等更丰富的结构。</para>
    /// <para><b>不保留多态类型信息</b>：未启用 <c>TypeNameHandling</c>，多态字段按声明类型序列化——
    /// 与 <c>JsonUtility</c> 在这件事上一致，不要指望它往返出派生类型（R3-SER-001）。</para>
    /// </summary>
    public sealed class NewtonsoftSerializer : ISerializer
    {
        /// <summary>格式标识：<c>"json"</c>。</summary>
        public string Format => "json";

        /// <inheritdoc/>
        public byte[] Serialize(object obj, Type type)
        {
            var json = JsonConvert.SerializeObject(obj, Formatting.None);
            return Encoding.UTF8.GetBytes(json);
        }

        /// <inheritdoc/>
        public object Deserialize(byte[] data, Type type)
        {
            var json = Encoding.UTF8.GetString(data);
            if (string.IsNullOrEmpty(json))
                return null; // 对齐 JsonUtility.FromJson("") 返回 null 的行为，避免空串时抛 JsonReaderException

            return JsonConvert.DeserializeObject(json, type);
        }
    }
}
