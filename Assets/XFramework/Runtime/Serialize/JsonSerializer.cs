using System;
using UnityEngine;

namespace XFramework.XSerialize
{
    /// <summary>
    /// 基于 Unity JsonUtility 的 JSON 序列化器，框架默认实现。
    /// </summary>
    public sealed class JsonSerializer : ISerializer
    {
        public string Format => "json";

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
