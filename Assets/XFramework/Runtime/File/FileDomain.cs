namespace XFramework.XFileManager
{
    /// <summary>
    /// 文件路径域枚举。通过域前缀屏蔽不同平台的物理路径差异。
    /// <para>借鉴 Godot 的 <c>user://</c> / <c>res://</c> 设计，使用枚举提供编译期安全。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>选择决策树：</b></para>
    /// <list type="bullet">
    /// <item>需要 <b>跨平台存档、云同步、账号绑定</b>？→ <see cref="SaveData"/></item>
    /// <item>存储 <b>机器级配置、着色器缓存、崩溃日志</b>？→ <see cref="AppData"/></item>
    /// <item>读取 <b>随包发布的策划数据、Lua 脚本、初始数据库</b>？→ <see cref="Streaming"/></item>
    /// <item>存储 <b>网络下载缓存、临时截图</b> 等可丢弃数据？→ <see cref="Cache"/></item>
    /// </list>
    /// <para><b>常见误区：</b></para>
    /// <list type="bullet">
    /// <item>❌ 把玩家存档放到 <see cref="AppData"/> —— Console 上卸载即丢，且无法云同步。</item>
    /// <item>❌ 把 <see cref="Streaming"/> 当作可写目录 —— 移动/Console 上包内资源只读。</item>
    /// <item>❌ 用 <see cref="Cache"/> 存重要数据 —— 操作系统可能随时清理。</item>
    /// </list>
    /// </remarks>
    public enum FileDomain
    {
        /// <summary>
        /// 持久化可读写数据目录。
        /// <para><b>映射：</b><c>Application.persistentDataPath</c></para>
        /// <para><b>读写权限：</b>可读可写</para>
        /// <para><b>各平台路径示例：</b></para>
        /// <list type="bullet">
        /// <item>Windows：<c>C:\Users\xxx\AppData\LocalLow\CompanyName\ProductName</c></item>
        /// <item>macOS：<c>~/Library/Application Support/CompanyName/ProductName</c></item>
        /// <item>Linux：<c>~/.config/unity3d/CompanyName/ProductName</c></item>
        /// <item>iOS：<c>/var/mobile/Containers/Data/Application/<UUID>/Documents</c></item>
        /// <item>Android：<c>/data/data/<bundle-id>/files</c></item>
        /// <item>Xbox/PS5/Switch：本地应用数据目录（非存档专用）</item>
        /// </list>
        /// <para><b>应放什么：</b>机器级配置、着色器缓存、崩溃日志、运行时生成的索引文件、无需云同步的本地数据。</para>
        /// <para><b>不应放什么：</b>玩家存档（请用 <see cref="SaveData"/>）。</para>
        /// </summary>
        AppData,

        /// <summary>
        /// 只读包内资源目录。
        /// <para><b>映射：</b><c>Application.streamingAssetsPath</c></para>
        /// <para><b>读写权限：</b>只读（所有平台）</para>
        /// <para><b>读取方式差异：</b></para>
        /// <list type="bullet">
        /// <item>桌面（Windows/Linux/macOS）：直接通过 <c>System.IO.File</c> 读取。</item>
        /// <item>移动（iOS/Android）：包内文件需通过 <c>UnityWebRequest</c> 访问，<c>MobileFileProvider</c> 已自动处理。</item>
        /// <item>Console：由第三方 <see cref="ConsoleFileProvider"/> 实现，通常需映射到 RomFS/Content 区域。</item>
        /// </list>
        /// <para><b>应放什么：</b>随包发布的 JSON/CSV 配置表、Lua/Python 脚本、初始 SQLite 数据库、不需要更新的静态资源。</para>
        /// <para><b>不应放什么：</b>任何需要在运行时修改的文件。</para>
        /// </summary>
        Streaming,

        /// <summary>
        /// 临时缓存目录。
        /// <para><b>映射：</b><c>Application.temporaryCachePath</c></para>
        /// <para><b>读写权限：</b>可读可写</para>
        /// <para><b>⚠️ 警告：此目录可被操作系统随时清理。</b></para>
        /// <para>不要在 <see cref="Cache"/> 中存储任何需要持久化的重要数据。
        /// 典型场景：网络下载的临时资源、临时截图、中间计算结果。</para>
        /// <para><b>应放什么：</b>HTTP 响应缓存、AssetBundle 热更临时文件、帧调试截图。</para>
        /// <para><b>不应放什么：</b>任何需要在应用重启或系统重启后仍存在的数据。</para>
        /// </summary>
        Cache,

        /// <summary>
        /// 控制台平台存档专用域。
        /// <para><b>桌面/移动平台的映射：</b>等同于 <see cref="AppData"/>（<c>Application.persistentDataPath</c>）。</para>
        /// <para><b>Console 平台的映射（由第三方 <see cref="ConsoleFileProvider"/> 实现）：</b></para>
        /// <list type="table">
        /// <item><term>Xbox</term><description>→ XGameSave / Connected Storage（<b>云同步 + Xbox Live 账号绑定</b>）</description></item>
        /// <item><term>PS5</term><description>→ <c>sceSaveData</c> API（<b>云同步 + PSN 账号绑定 + 配额限制</b>）</description></item>
        /// <item><term>Switch</term><description>→ <c>nn::fs</c> 托管保存目录（<b>账号隔离 + 系统级备份</b>）</description></item>
        /// </list>
        /// <para><b>Console 平台上 SaveData 与 AppData 的关键区别：</b></para>
        /// <list type="bullet">
        /// <item>✅ 平台自动云同步（Xbox Live / PSN）。</item>
        /// <item>✅ 账号隔离 —— 每个用户的存档独立且不可互见。</item>
        /// <item>✅ 卸载应用后数据保留。</item>
        /// <item>✅ 满足 Console 平台的 TRC/XR 认证要求。</item>
        /// </list>
        /// <para><b>应放什么：</b>玩家进度、存档文件、游戏内购买记录、需要跟随账号的玩家设置。</para>
        /// <para><b>不应放什么：</b>机器级缓存、着色器、崩溃日志（请用 <see cref="AppData"/>）。</para>
        /// </summary>
        SaveData,
    }
}