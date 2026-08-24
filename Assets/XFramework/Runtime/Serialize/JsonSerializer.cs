using System;
using UnityEngine;

namespace XFramework.XSerialize
{
    /// <summary>
    /// 基于 Unity JsonUtility 的 JSON 序列化器，遗留实现。
    /// <para>默认序列化器已切换为 <see cref="NewtonsoftSerializer"/>（format = "json"），
    /// 本类以 format = "json-utility" 保留，用于显式读写旧 JsonUtility 格式存档（兼容旧档）。</para>
    /// </summary>
    public sealed class JsonSerializer : ISerializer
    {
        public string Format => "json-utility";

        public byte[] Serialize(object obj, Type type)
        {
            var json = JsonUtility.ToJson(obj, false);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        public object Deserialize(byte[] data, Type type)
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            return JsonUtility.FromJson(json, type);
        }
    }
}
