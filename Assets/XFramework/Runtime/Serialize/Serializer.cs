using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XSerialize
{
    /// <summary>
    /// 序列化器静态门面，管理所有已注册的 <see cref="ISerializer"/>。
    /// </summary>
    /// <remarks>
    /// <para>框架初始化时自动注册 <see cref="NewtonsoftSerializer"/> 作为默认实现（format = "json"），
    /// 并注册 <see cref="JsonSerializer"/>（format = "json-utility"）以兼容读写旧 JsonUtility 格式存档。</para>
    /// <para>第三方可调用 <see cref="Register"/> 注册自定义序列化器，通过 format 标识查找。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 注册自定义格式
    /// Serializer.Register(new MyMessagePackSerializer());
    ///
    /// // 获取序列化器
    /// var ser = Serializer.Get("msgpack");
    /// var bytes = ser.Serialize(myData, typeof(MyData));
    /// </code>
    /// </example>
    public static class Serializer
    {
        #region Fields

        private static readonly Dictionary<string, ISerializer> Serializers = new();
        private static bool _initialized;

        #endregion

        #region Initialization

        /// <summary>
        /// 初始化序列化器模块，注册内置的 NewtonsoftSerializer（默认）与 JsonSerializer（遗留）。
        /// <para>由框架自动调用，也可手动调用以重新初始化。</para>
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
                return;

            Register(new NewtonsoftSerializer()); // 默认 "json"
            Register(new JsonSerializer());       // 遗留 "json-utility"，兼容旧 JsonUtility 格式存档
            _initialized = true;
        }

        /// <summary>
        /// 反初始化，清空所有已注册的序列化器。
        /// </summary>
        public static void Shutdown()
        {
            Serializers.Clear();
            _initialized = false;
        }

        #endregion

        #region Public API

        /// <summary>
        /// 注册一个序列化器实例。若已存在相同 format 的序列化器，将被覆盖。
        /// </summary>
        /// <param name="serializer">序列化器实例，不可为 null。</param>
        public static void Register(ISerializer serializer)
        {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));
            if (string.IsNullOrEmpty(serializer.Format))
                throw new ArgumentException("Serializer.Format 不可为空。", nameof(serializer));

            Serializers[serializer.Format] = serializer;
        }

        /// <summary>
        /// 注销指定 format 的序列化器。
        /// </summary>
        /// <returns>是否成功移除。</returns>
        public static bool Unregister(string format)
        {
            return Serializers.Remove(format);
        }

        /// <summary>
        /// 根据 format 获取序列化器。
        /// </summary>
        /// <param name="format">序列化格式标识，如 "json"。</param>
        /// <returns>对应的序列化器。</returns>
        /// <exception cref="KeyNotFoundException">未找到指定 format 的序列化器。</exception>
        public static ISerializer Get(string format)
        {
            if (Serializers.TryGetValue(format, out var serializer))
                return serializer;

            throw new KeyNotFoundException(
                $"[XSerialize] 未找到 format = '{format}' 的序列化器。请先调用 Serializer.Register 注册。");
        }

        /// <summary>
        /// 尝试获取指定 format 的序列化器。
        /// </summary>
        /// <returns>是否成功获取。</returns>
        public static bool TryGet(string format, out ISerializer serializer)
        {
            return Serializers.TryGetValue(format, out serializer);
        }

        /// <summary>
        /// 获取默认序列化器（format = "json"）。
        /// </summary>
        /// <exception cref="KeyNotFoundException">默认序列化器未注册（Initialize 未被调用）。</exception>
        public static ISerializer Default
        {
            get
            {
                if (Serializers.TryGetValue("json", out var serializer))
                    return serializer;

                throw new InvalidOperationException(
                    "[XSerialize] 默认序列化器不可用，请确认 Serializer.Initialize() 已被调用。");
            }
        }

        /// <summary>
        /// 模块是否已初始化。
        /// </summary>
        public static bool IsInitialized => _initialized;

        #endregion
    }
}