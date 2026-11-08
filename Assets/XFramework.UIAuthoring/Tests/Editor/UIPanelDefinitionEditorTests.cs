using NUnit.Framework;

namespace XFramework.XUI.Authoring.Editor.Tests
{
    [TestFixture]
    public sealed class UIPanelDefinitionEditorTests
    {
        [Test]
        public void EditorType_IsPublic()
        {
            Assert.That(typeof(UIPanelDefinitionEditor).IsPublic, Is.True);
        }
    }
}
