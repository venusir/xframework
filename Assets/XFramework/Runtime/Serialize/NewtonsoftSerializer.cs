using System;
using System.Text;
using Newtonsoft.Json;

namespace XFramework.XSerialize
{
    /// <summary>
    /// 基于 Newtonsoft.Json 的 JSON 序列化器，框架默认实现。
    /// <para>相比 <see cref="JsonSerializer"/>（JsonUtility）支持 Dictionary、多态等更丰富的结构。</para>
    /// </summary>
    public sealed class NewtonsoftSerializer : ISerializer
    {
        public string Format => "json";

        public byte[] Serialize(object obj, Type type)
        {
            var json = JsonConvert.SerializeObject(obj, Formatting.None);
            return Encoding.UTF8.GetBytes(json);
        }

        public object Deserialize(byte[] data, Type type)
        {
            var json = Encoding.UTF8.GetString(data);
            if (string.IsNullOrEmpty(json))
                return null; // 对齐 JsonUtility.FromJson("") 返回 null 的行为，避免空串时抛 JsonReaderException

            return JsonConvert.DeserializeObject(json, type);
        }
    }
}
