using NUnit.Framework;
using UnityEngine;
using XFramework.XUI.Authoring;

namespace XFramework.XUI.Authoring.Editor.Tests
{
    [TestFixture]
    public sealed class UIPanelDefinitionTests
    {
        [Test]
        public void NewDefinition_HasEmptyLists()
        {
            var definition = ScriptableObject.CreateInstance<UIPanelDefinition>();
            try
            {
                Assert.That(definition.TextBindings, Is.Empty);
                Assert.That(definition.ActionBindings, Is.Empty);
                Assert.That(definition.TabGroups, Is.Empty);
                Assert.That(definition.Version, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void KindValues_AreStable()
        {
            Assert.That((int)UIPanelDefinitionKind.Panel, Is.EqualTo(0));
            Assert.That((int)UIPanelDefinitionKind.Hud, Is.EqualTo(1));
            Assert.That((int)UIPanelDefinitionKind.Dialog, Is.EqualTo(2));
        }

        [Test]
        public void ActionKindValues_AreStable()
        {
            Assert.That((int)UIActionKind.CloseSelf, Is.EqualTo(0));
            Assert.That((int)UIActionKind.Custom, Is.EqualTo(12));
            Assert.That((int)UIActionKind.CloseWithResult, Is.EqualTo(13));
            Assert.That((int)UIActionKind.HideSelf, Is.EqualTo(14));
        }

        [Test]
        public void TipConfig_ConvertsWithoutNullableBoxing()
        {
            var config = new UITipActionConfig
            {
                UseWorldPos = false,
                Color = Color.red,
                Duration = 1.5f,
                FloatDistance = 12f,
                FontSize = 24f,
            };

            var tip = config.ToTipConfig();
            Assert.That(tip.WorldPos.HasValue, Is.False);
            Assert.That(tip.Color, Is.EqualTo(Color.red));
            Assert.That(tip.Duration, Is.EqualTo(1.5f));
            Assert.That(tip.FloatDistance, Is.EqualTo(12f));
            Assert.That(tip.FontSize, Is.EqualTo(24f));
        }
    }
}
