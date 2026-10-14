using System;
using System.Collections.Generic;

namespace XFramework.XSettings
{
    /// <summary>
    /// 字段句柄重放注册表的会话边界协调器：让非泛型的会话复位入口能够清空**所有已封闭**
    /// <see cref="SettingRefRegistry{T}"/> 的表。
    /// <para><b>为什么需要它：</b><see cref="SettingRefRegistry{T}"/> 是泛型静态类，每个封闭 <c>T</c> 各有一份
    /// 私有静态表，任何非泛型代码都无法遍历它们（反射扫程序集不可行、也违背「封闭泛型即类型索引」的取舍）。
    /// 本协调器照抄 <c>CollectionPoolManager</c> 的既有形状：每个封闭泛型在**静态构造**时把自己的清理委托
    /// 登记进来，未被触碰过的类型不登记、也不需要清理。</para>
    /// <para><b>只由会话边界调用</b>（<see cref="SettingsManager.ResetForNewSession"/>）；
    /// <see cref="SettingsManager.Destroy"/> 刻意不清（见注册表的类型注释）。</para>
    /// <para><b>线程：</b>与注册表一致，只允许主线程。</para>
    /// </summary>
    internal static class SettingRefSessionCoordinator
    {
        #region Private Fields

        /// <summary>各已封闭 <see cref="SettingRefRegistry{T}"/> 的会话复位委托，按静态构造触发顺序排列。</summary>
        private static readonly List<Action> ClearActions = new();

        #endregion

        #region Internal

        /// <summary>
        /// 登记一个封闭泛型的会话复位委托。由 <see cref="SettingRefRegistry{T}"/> 的静态构造函数调用，不应手动使用。
        /// </summary>
        /// <param name="clearAction">该泛型的清表 + 递增代际号的委托。</param>
        internal static void Register(Action clearAction)
        {
            if (clearAction == null)
            {
                return;
            }

            ClearActions.Add(clearAction);
        }

        /// <summary>
        /// 对所有已被触碰过的设置类型执行会话复位（清表并递增代际号）。
        /// </summary>
        internal static void ClearAll()
        {
            // foreach 而非索引循环：本表只在静态构造里追加，遍历期间不会有人再登记
            // （清表动作本身不触碰其它泛型，故不会触发新的静态构造）
            foreach (var clearAction in ClearActions)
            {
                clearAction();
            }
        }

        #endregion
    }
}
