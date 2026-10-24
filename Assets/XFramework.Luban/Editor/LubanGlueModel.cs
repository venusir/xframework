using System;
using System.Collections.Generic;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// Luban 表的注册形状。
    /// </summary>
    internal enum LubanTableShape
    {
        /// <summary>默认 map 表：公开 DataMap。</summary>
        Map = 0,

        /// <summary>单行表：公开 Data。</summary>
        SingleRow = 1,

        /// <summary><c>mode="list"</c> 带索引：私有字典 + DataList。</summary>
        IndexedList = 2,

        /// <summary>纯列表表：只有 DataList，无主键。</summary>
        PureList = 3,
    }

    /// <summary>
    /// 从生成的表类中解析出的一张表。
    /// </summary>
    internal sealed class LubanGeneratedTable
    {
        internal string PropertyName;
        internal string TableTypeFullName;
        internal string OutputDataName;
        internal LubanTableShape Shape;
        internal string RowTypeFullName;
        internal string RowNamespace;
        internal string RowTypeName;
        internal bool RowIsClass;
        internal string RowAccessibility;
        internal string KeyTypeText;
        internal string IdExpression;
        internal string RowExpression;
    }

    /// <summary>
    /// 从 Luban 生成代码解析出的整包模型。
    /// </summary>
    internal sealed class LubanGeneratedModel
    {
        internal string TablesNamespace;
        internal string TablesClassName;
        internal string TablesFullName;
        internal List<LubanGeneratedTable> Tables = new();
    }

    /// <summary>
    /// 解析 / 生成胶水时的可读错误。
    /// </summary>
    internal sealed class LubanGlueException : Exception
    {
        internal LubanGlueException(string message)
            : base(message)
        {
        }
    }
}
