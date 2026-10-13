using System;
using NUnit.Framework;
using XFramework.XAsset;
using YooAsset;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="AssetOperationStatus"/> 与 YooAsset <c>EOperationStatus</c> 的换算（唯一实现在
    /// <see cref="AssetOperationStatusMapper"/>）。
    /// <para><b>为什么值得一条用例</b>：四个句柄的 <c>Status</c> 由上游枚举换成框架自有枚举时，
    /// 「语义与取值时机完全不变」这句承诺**全靠这张映射表**——它是公开面的行为承诺，不是内部细节。
    /// 上游将来新增成员时的回落规则（**未知值归 `None`，绝不归 `Succeed`**）也由这里钉住。</para>
    /// </summary>
    [TestFixture]
    public class AssetOperationStatusTests
    {
        [Test]
        public void Mapper_MapsEveryUpstreamValueOneToOne()
        {
            Assert.AreEqual(AssetOperationStatus.None, AssetOperationStatusMapper.From(EOperationStatus.None));
            Assert.AreEqual(AssetOperationStatus.Processing, AssetOperationStatusMapper.From(EOperationStatus.Processing));
            Assert.AreEqual(AssetOperationStatus.Succeed, AssetOperationStatusMapper.From(EOperationStatus.Succeed));
            Assert.AreEqual(AssetOperationStatus.Failed, AssetOperationStatusMapper.From(EOperationStatus.Failed));
        }

        [Test]
        public void Mapper_UnknownValueFallsBackToNone_NotSucceed()
        {
            // 上游新增成员时的兜底：宁可报「尚未开始」，也不能把未知状态说成成功——
            // 回落成 Succeed 会把失败当成功，那是最坏的一种错。
            Assert.AreEqual(AssetOperationStatus.None, AssetOperationStatusMapper.From((EOperationStatus)999));
            Assert.AreEqual(AssetOperationStatus.None, AssetOperationStatusMapper.From((EOperationStatus)(-1)));
        }

        [Test]
        public void Members_MatchUpstreamNames_SoMigrationStaysMechanical()
        {
            // 与上游同名是**刻意**的：迁移只需换类型名，不必逐处对照语义，也不与 YooAsset 文档
            // 产生第二套说法。改名会让使用方的迁移从「换类型名」升级成「逐处核对」。
            var names = Enum.GetNames(typeof(AssetOperationStatus));
            Array.Sort(names, StringComparer.Ordinal);
            Assert.AreEqual("Failed,None,Processing,Succeed", string.Join(",", names));
        }
    }
}
