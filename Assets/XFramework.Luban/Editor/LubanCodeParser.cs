using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// 从 Luban 5.1.0 生成的 C# 代码中解析表模型。纯文本解析、不依赖 Luban 程序集；
    /// 形状不认识时给「文件:行号」并拒绝写任何产物。
    /// </summary>
    internal static class LubanCodeParser
    {
        private static readonly Regex NamespaceRegex = new(
            @"^\s*namespace\s+(?<ns>[\w\.]+)\s*;?\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex TypeRegex = new(
            @"\b(?<acc>public|internal)\s+(?:(?:sealed|abstract|static|readonly|ref|unsafe)\s+)*(?:partial\s+)?(?<kind>class|struct)\s+(?<name>\w+)\b",
            RegexOptions.Compiled);

        private static readonly Regex LoaderRegex = new(
            @"(?<prop>\w+)\s*=\s*new\s+(?<type>[\w\.]+)\s*\(\s*loader\(\s*""(?<output>[^""]+)""\s*\)\s*\)",
            RegexOptions.Compiled);

        private static readonly Regex TablesLoaderTypeRegex = new(
            @"Func\s*<\s*string\s*,\s*(?<loader>[\w\.]+)\s*>\s+loader\b",
            RegexOptions.Compiled);

        private static readonly Regex DictionaryFieldRegex = new(
            @"\bDictionary<(?<args>.+?)>\s+(?<name>_dataMap\w*)\s*;",
            RegexOptions.Compiled);

        private static readonly Regex DataMapPropertyRegex = new(
            @"\bIReadOnlyDictionary<.+?>\s+DataMap\s*=>",
            RegexOptions.Compiled);

        private static readonly Regex DataPropertyRegex = new(
            @"\bpublic\s+(?<type>[\w\.]+)\s+Data\s*=>",
            RegexOptions.Compiled);

        private static readonly Regex DataListPropertyRegex = new(
            @"\bIReadOnlyList<(?<type>.+?)>\s+DataList\s*=>",
            RegexOptions.Compiled);

        private static readonly Regex SimpleIdentifierRegex = new(
            @"^[A-Za-z_]\w*$",
            RegexOptions.Compiled);

        private static readonly Regex SimpleTupleRegex = new(
            @"^\(\s*[A-Za-z_]\w*\s*(,\s*[A-Za-z_]\w*\s*)+\)$",
            RegexOptions.Compiled);

        internal static LubanGeneratedModel Parse(string codeDir, string tablesClassName)
        {
            if (string.IsNullOrWhiteSpace(codeDir))
            {
                throw new ArgumentException("代码输出目录为空。", nameof(codeDir));
            }

            if (!Directory.Exists(codeDir))
            {
                throw new LubanGenerationException("代码输出目录不存在：" + codeDir);
            }

            if (string.IsNullOrWhiteSpace(tablesClassName))
            {
                tablesClassName = "Tables";
            }

            string[] files = Directory.GetFiles(codeDir, "*.cs", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0)
            {
                throw new LubanGenerationException("代码输出目录下没有 .cs 文件：" + codeDir);
            }

            var types = BuildTypeIndex(files);
            TypeInfo tablesType = FindUniqueType(types, tablesClassName);
            if (tablesType == null)
            {
                throw new LubanGenerationException(
                    "未找到 Luban 生成的 Tables 类 '" + tablesClassName + "'（目录：" + codeDir + "）。");
            }

            var model = new LubanGeneratedModel
            {
                TablesNamespace = tablesType.Namespace,
                TablesClassName = tablesType.SimpleName,
                TablesFullName = tablesType.FullName,
                LoaderKind = DetectLoaderKind(tablesType.Text),
            };

            var matches = LoaderRegex.Matches(tablesType.Text);
            if (matches.Count == 0)
            {
                throw Error(tablesType.FilePath, tablesType.Text, 0,
                    "Tables 构造函数中没有找到 loader(\"...\") 调用。");
            }

            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                string property = match.Groups["prop"].Value;
                string typeText = match.Groups["type"].Value;
                string output = match.Groups["output"].Value;

                var tableType = ResolveType(
                    types, typeText, tablesType.Namespace, tablesType.FilePath, tablesType.Text, match.Index);

                model.Tables.Add(ParseTable(types, tableType, property, output));
            }

            return model;
        }

        private static LubanLoaderKind DetectLoaderKind(string tablesText)
        {
            var match = TablesLoaderTypeRegex.Match(tablesText);
            if (!match.Success)
            {
                return LubanLoaderKind.Unknown;
            }

            string loader = match.Groups["loader"].Value;
            int dot = loader.LastIndexOf('.');
            string simple = dot >= 0 ? loader.Substring(dot + 1) : loader;
            return simple switch
            {
                "ByteBuf" => LubanLoaderKind.ByteBuf,
                "JSONNode" => LubanLoaderKind.JsonNode,
                _ => LubanLoaderKind.Unknown,
            };
        }

        private static List<TypeInfo> BuildTypeIndex(string[] files)
        {
            var types = new List<TypeInfo>();

            foreach (var file in files)
            {
                string text = File.ReadAllText(file);
                var nsMatch = NamespaceRegex.Match(text);
                string ns = nsMatch.Success ? nsMatch.Groups["ns"].Value : string.Empty;

                var matches = TypeRegex.Matches(text);
                for (int i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];
                    types.Add(new TypeInfo
                    {
                        FilePath = file,
                        Namespace = ns,
                        SimpleName = match.Groups["name"].Value,
                        IsClass = match.Groups["kind"].Value == "class",
                        Accessibility = match.Groups["acc"].Value,
                        Text = text,
                    });
                }
            }

            return types;
        }

        private static TypeInfo FindUniqueType(List<TypeInfo> types, string simpleName)
        {
            TypeInfo found = null;
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i].SimpleName != simpleName)
                {
                    continue;
                }

                if (found != null)
                {
                    throw new LubanGenerationException(
                        "类型名 '" + simpleName + "' 在生成代码中出现多次：\n" +
                        found.FilePath + "\n" + types[i].FilePath);
                }

                found = types[i];
            }

            return found;
        }

        private static TypeInfo ResolveType(
            List<TypeInfo> types,
            string typeText,
            string contextNamespace,
            string file,
            string text,
            int index)
        {
            string simpleName = typeText;
            int dot = typeText.LastIndexOf('.');
            if (dot >= 0)
            {
                simpleName = typeText.Substring(dot + 1);
            }

            var candidates = new List<TypeInfo>();
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i].SimpleName == simpleName)
                {
                    candidates.Add(types[i]);
                }
            }

            if (candidates.Count == 0)
            {
                throw Error(file, text, index, "无法解析类型 '" + typeText + "'。");
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            string fullCandidate = string.IsNullOrEmpty(contextNamespace)
                ? typeText
                : contextNamespace + "." + typeText;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].FullName == fullCandidate)
                {
                    return candidates[i];
                }
            }

            TypeInfo suffixMatch = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].FullName.EndsWith("." + typeText, StringComparison.Ordinal))
                {
                    continue;
                }

                if (suffixMatch != null)
                {
                    throw Error(file, text, index, "类型 '" + typeText + "' 存在多个候选，无法消歧。");
                }

                suffixMatch = candidates[i];
            }

            if (suffixMatch != null)
            {
                return suffixMatch;
            }

            throw Error(file, text, index, "类型 '" + typeText + "' 存在多个候选，无法消歧。");
        }

        private static LubanGeneratedTable ParseTable(
            List<TypeInfo> types,
            TypeInfo tableType,
            string property,
            string output)
        {
            string text = tableType.Text;
            var table = new LubanGeneratedTable
            {
                PropertyName = property,
                OutputDataName = output,
            };

            var dictionaryFields = DictionaryFieldRegex.Matches(text);
            bool hasDataMap = DataMapPropertyRegex.IsMatch(text);
            var dataMatch = DataPropertyRegex.Match(text);
            var dataListMatch = DataListPropertyRegex.Match(text);

            if (hasDataMap)
            {
                if (dictionaryFields.Count != 1)
                {
                    throw Error(tableType.FilePath, text, 0,
                        "map 表应恰有一个私有字典字段，实际找到 " + dictionaryFields.Count + " 个。");
                }

                table.Shape = LubanTableShape.Map;
                FillKeyed(types, table, tableType, dictionaryFields[0]);
            }
            else if (dataMatch.Success)
            {
                table.Shape = LubanTableShape.SingleRow;
                var row = ResolveType(
                    types,
                    dataMatch.Groups["type"].Value,
                    tableType.Namespace,
                    tableType.FilePath,
                    text,
                    dataMatch.Index);
                FillRow(table, row);

                if (!row.IsClass)
                {
                    throw Error(tableType.FilePath, text, dataMatch.Index,
                        "单行表行类型 '" + row.FullName + "' 是 struct，无法注册为 ConfigManager Global。");
                }
            }
            else if (dictionaryFields.Count == 1)
            {
                table.Shape = LubanTableShape.IndexedList;
                FillKeyed(types, table, tableType, dictionaryFields[0]);
            }
            else if (dictionaryFields.Count > 1)
            {
                throw Error(tableType.FilePath, text, 0,
                    "表 '" + property + "' 有多个独立索引（index=\"a,b,...\"），" +
                    "无法表达为单一 IConfigRow<TKey>；请改用联合索引（index=\"a+b\"）或手写适配。");
            }
            else if (dataListMatch.Success)
            {
                table.Shape = LubanTableShape.PureList;
                var row = ResolveType(
                    types,
                    dataListMatch.Groups["type"].Value,
                    tableType.Namespace,
                    tableType.FilePath,
                    text,
                    dataListMatch.Index);
                FillRow(table, row);
            }
            else
            {
                throw Error(tableType.FilePath, text, 0,
                    "无法识别表 '" + property + "' 的形状（既没有 DataMap/Data，也没有索引字典/DataList）。");
            }

            return table;
        }

        private static void FillKeyed(
            List<TypeInfo> types,
            LubanGeneratedTable table,
            TypeInfo tableType,
            Match dictionaryField)
        {
            string args = dictionaryField.Groups["args"].Value;
            if (!TrySplitDictionaryArguments(args, out string keyType, out string valueType))
            {
                throw Error(tableType.FilePath, tableType.Text, dictionaryField.Index,
                    "无法拆解字典类型 'Dictionary<" + args + ">'。");
            }

            var row = ResolveType(
                types,
                valueType,
                tableType.Namespace,
                tableType.FilePath,
                tableType.Text,
                dictionaryField.Index);
            FillRow(table, row);
            table.KeyTypeText = keyType;

            string fieldName = dictionaryField.Groups["name"].Value;
            string addPattern = @"\b" + Regex.Escape(fieldName) + @"\.Add\(\s*(?<key>.+?)\s*,\s*_v\s*\)";
            var addMatch = Regex.Match(tableType.Text, addPattern, RegexOptions.Singleline);
            if (!addMatch.Success)
            {
                throw Error(tableType.FilePath, tableType.Text, dictionaryField.Index,
                    "无法找到 " + fieldName + ".Add(<key>, _v) 的建索引表达式。");
            }

            string keyExpression = addMatch.Groups["key"].Value.Trim();
            string idExpression = keyExpression.Replace("_v.", string.Empty).Trim();
            string rowExpression = keyExpression.Replace("_v.", "row.").Trim();

            if (!IsSimpleKeyExpression(idExpression))
            {
                throw Error(tableType.FilePath, tableType.Text, addMatch.Index,
                    "键表达式 '" + keyExpression + "' 不是简单的字段访问/元组，无法自动生成行接口实现；" +
                    "请手写 partial 实现并设置 GenerateRowInterfaces=false。");
            }

            table.IdExpression = idExpression;
            table.RowExpression = rowExpression;
        }

        private static void FillRow(LubanGeneratedTable table, TypeInfo row)
        {
            table.RowTypeFullName = row.FullName;
            table.RowNamespace = row.Namespace;
            table.RowTypeName = row.SimpleName;
            table.RowIsClass = row.IsClass;
            table.RowAccessibility = row.Accessibility;
        }

        private static bool TrySplitDictionaryArguments(
            string args,
            out string key,
            out string value)
        {
            key = null;
            value = null;

            int depth = 0;
            int split = -1;

            for (int i = 0; i < args.Length; i++)
            {
                char c = args[i];
                if (c == '<' || c == '(' || c == '[')
                {
                    depth++;
                }
                else if (c == '>' || c == ')' || c == ']')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    if (split >= 0)
                    {
                        return false;
                    }

                    split = i;
                }
            }

            if (split < 0)
            {
                return false;
            }

            key = args.Substring(0, split).Trim();
            value = args.Substring(split + 1).Trim();
            return key.Length > 0 && value.Length > 0;
        }

        private static bool IsSimpleKeyExpression(string expression)
        {
            return SimpleIdentifierRegex.IsMatch(expression) || SimpleTupleRegex.IsMatch(expression);
        }

        private static LubanGenerationException Error(string file, string text, int index, string message)
        {
            int line = 1;
            int limit = Math.Min(index, text.Length);
            for (int i = 0; i < limit; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return new LubanGenerationException(file + ":" + line + ": " + message);
        }

        private sealed class TypeInfo
        {
            internal string FilePath;
            internal string Namespace;
            internal string SimpleName;
            internal bool IsClass;
            internal string Accessibility;
            internal string Text;

            internal string FullName =>
                string.IsNullOrEmpty(Namespace) ? SimpleName : Namespace + "." + SimpleName;
        }
    }
}
