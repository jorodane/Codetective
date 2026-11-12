using Codetective.Terminal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Tests.Terminal
{
    public sealed class TextInputFocusTests
    {
        [Test]
        public void SelectingAnInputBlocksWorldUseBeforeActivationAndSelectingAButtonReleasesIt()
        {
            var events = new GameObject("Events", typeof(EventSystem));
            var input = new GameObject("Input", typeof(RectTransform), typeof(InputField));
            var button = new GameObject("Button", typeof(RectTransform), typeof(Button));
            try
            {
                EventSystem.current.SetSelectedGameObject(input);
                Assert.That(TextInputFocus.IsActive, Is.True);
                input.GetComponent<InputField>().readOnly = true;
                Assert.That(TextInputFocus.IsActive, Is.True);
                EventSystem.current.SetSelectedGameObject(button);
                Assert.That(TextInputFocus.IsActive, Is.False);
            }
            finally { Object.DestroyImmediate(input); Object.DestroyImmediate(button); Object.DestroyImmediate(events); }
        }
    }
}
