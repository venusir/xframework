using System;
using System.Globalization;
using XFramework.XFileManager;

namespace XFramework.XSave
{
    /// <summary>
    /// 存档路径与标识校验工具（静态纯函数）。
    /// <para>把「什么算合法 playerId / 槽位号」集中到一处：这些规则同时被门面（快速失败）、
    /// 按玩家查询、删除玩家与路径构造使用，分散实现必然漂移。</para>
    /// <para>全部为同步纯静态方法，以便门面在进入异步管线之前就能抛出——
    /// 若校验写在 <c>async</c> 方法体内，异常会被状态机包进返回的 UniTask，调用方拿不到同步失败。</para>
    /// </summary>
    internal static class SavePathUtility
    {
        #region Constants

        /// <summary>槽位文件名前缀。</summary>
        internal const string SlotFilePrefix = "slot_";

        /// <summary>槽位文件名后缀。</summary>
        internal const string SlotFileSuffix = ".save";

        /// <summary>
        /// playerId 长度上限。
        /// <para>作为单段目录名，过长会把完整路径推向 MAX_PATH 上限。</para>
        /// </summary>
        private const int MaxPlayerIdLength = 64;

        /// <summary>
        /// playerId 中不允许出现的字符。
        /// <para><b>刻意不用 <see cref="System.IO.Path.GetInvalidFileNameChars"/>：</b>
        /// 它在 Linux 上只返回 <c>\0</c> 与 <c>/</c>，会让 <c>a:b</c> 这类 ID 在 Linux 建得出、
        /// 在 Windows 读不了——存档一旦写入就无法跨平台迁移。这里用固定集合保证各平台一致。</para>
        /// </summary>
        private static readonly char[] IllegalPlayerIdChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        #endregion

        #region Validation

        /// <summary>
        /// 校验 playerId 可作为单段目录名。
        /// <para>规则：非空且非全空白、长度不超上限、不为 <c>.</c> 或 <c>..</c>、
        /// 不含文件系统非法字符、不以点或空格结尾。</para>
        /// </summary>
        /// <param name="playerId">待校验的玩家 ID。</param>
        /// <exception cref="ArgumentException"><paramref name="playerId"/> 非法时抛出。</exception>
        internal static void ValidatePlayerId(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("[Save] playerId 不能为空或全空白。", nameof(playerId));

            if (playerId.Length > MaxPlayerIdLength)
                throw new ArgumentException(
                    $"[Save] playerId 过长（{playerId.Length} 字符 > 上限 {MaxPlayerIdLength}）。", nameof(playerId));

            // "." 与 ".." 必须显式拒绝：二者都能穿过 FileManager 的路径沙箱（它只拦 .. 段），
            // "." 会让存档落到域根从而绕开玩家隔离，而 DeletePlayer(".") 会删掉域根下的全部存档
            if (playerId == "." || playerId == "..")
                throw new ArgumentException($"[Save] 非法 playerId '{playerId}'：不能为 '.' 或 '..'。", nameof(playerId));

            if (playerId.IndexOfAny(IllegalPlayerIdChars) >= 0)
                throw new ArgumentException(
                    $"[Save] 非法 playerId '{playerId}'：不允许包含 < > : \" / \\ | ? * 等字符。", nameof(playerId));

            // 结尾的点或空格会被 Windows 静默裁掉，导致 "Alice" 与 "Alice." 落到同一目录
            var last = playerId[playerId.Length - 1];
            if (last == '.' || last == ' ')
                throw new ArgumentException($"[Save] 非法 playerId '{playerId}'：不能以点或空格结尾。", nameof(playerId));
        }

        /// <summary>
        /// 校验槽位号合法（<c>&gt;= 0</c>）。
        /// <para>负数会写出 <c>slot_-1.save</c> 这类文件名，与「解析失败」的哨兵值撞车。</para>
        /// </summary>
        /// <param name="slot">槽位号。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> 小于 0 时抛出。</exception>
        internal static void ValidateSlot(int slot)
        {
            if (slot < 0)
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "[Save] 槽位号必须为非负整数。");
        }

        #endregion

        #region Parsing

        /// <summary>
        /// 尝试从存档文件相对路径解析槽位号。
        /// <para><b>严格解析：</b>文件名须为 <c>slot_&lt;非负整数&gt;.save</c>（可带 <c>playerId/</c> 前缀）。
        /// <c>slot_abc.save</c>、<c>slot_.save</c>、<c>slot_-1.save</c> 一律解析失败。</para>
        /// <para>与删除路径所用的宽松判断（只看前缀后缀）分工不同：删除要清掉所有 <c>slot_</c> 前缀残留，
        /// 枚举则只列合法槽位。两者语义不同，刻意不做统一。</para>
        /// </summary>
        /// <param name="path">存档文件相对路径。</param>
        /// <param name="slot">解析出的槽位号；失败时为 <c>-1</c>。</param>
        /// <returns>解析成功返回 <c>true</c>。</returns>
        internal static bool TryParseSlot(string path, out int slot)
        {
            slot = -1;

            if (string.IsNullOrEmpty(path))
                return false;

            var fileName = XFileManager.FilePathUtility.GetFileNameFromPath(path);
            if (!fileName.StartsWith(SlotFilePrefix, StringComparison.Ordinal)
                || !fileName.EndsWith(SlotFileSuffix, StringComparison.Ordinal))
                return false;

            var start = SlotFilePrefix.Length;
            var end = fileName.Length - SlotFileSuffix.Length;
            if (end <= start)
                return false;

            // 文化无关 + 仅数字：NumberStyles.None 顺带拒绝正负号与空白，
            // 使 "-1"、" 1"、"+1" 这类写法无法蒙混过关
            var digits = fileName.Substring(start, end - start);
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                return false;

            slot = parsed;
            return true;
        }

        #endregion

        #region Path Building

        /// <summary>
        /// 构造槽位文件名。
        /// </summary>
        /// <param name="slot">槽位号。</param>
        /// <returns>形如 <c>slot_3.save</c> 的文件名。</returns>
        internal static string BuildSlotFileName(int slot)
        {
            // 文化无关格式化：默认的 int.ToString() 在部分文化下负号不是 '-'，
            // 会写出无法用 int.TryParse 往返解析的文件名
            return string.Concat(SlotFilePrefix, slot.ToString(CultureInfo.InvariantCulture), SlotFileSuffix);
        }

        /// <summary>
        /// 构造槽位文件的相对路径（纯函数，不读取当前玩家上下文）。
        /// </summary>
        /// <param name="playerId">玩家 ID。为 <c>null</c> 时直接落在域根目录。</param>
        /// <param name="slot">槽位号。</param>
        /// <returns>形如 <c>Alice/slot_3.save</c> 或 <c>slot_3.save</c> 的相对路径。</returns>
        /// <exception cref="ArgumentException"><paramref name="playerId"/> 非法时抛出。</exception>
        internal static string BuildSlotPath(string playerId, int slot)
        {
            var fileName = BuildSlotFileName(slot);
            if (playerId == null)
                return fileName;

            ValidatePlayerId(playerId);
            return string.Concat(playerId, "/", fileName);
        }

        #endregion
    }
}
