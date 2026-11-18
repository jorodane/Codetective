#if UNITY_EDITOR
using System.Reflection;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal.Tests.UI
{
    public sealed class MailNotificationTests
    {
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static MonitorPageHost Monitor(GameObject root, KernelRuntime runtime, params MonitorPage[] pages)
        {
            var navigation = new GameObject("Navigation", typeof(RectTransform)); navigation.transform.SetParent(root.transform, false);
            navigation.GetComponent<RectTransform>().sizeDelta = new Vector2(1090, 44);
            foreach (MonitorPage page in pages) page.transform.SetParent(root.transform, false);
            MonitorPageHost host = root.AddComponent<MonitorPageHost>(); Set(host, "navigationRoot", navigation.GetComponent<RectTransform>());
            host.AttachRuntime(runtime); host.Initialize(); return host;
        }
        [Test]
        public void EnvelopeBorrowsTheCrtAndRestoresTheOriginalPageAndDraftWithoutReadingMail()
        {
            var root = new GameObject("Monitor", typeof(RectTransform)); root.SetActive(false);
            var kernel = new GameObject("Kernel", typeof(RectTransform), typeof(MonitorPage)); kernel.GetComponent<MonitorPage>().Configure("kernel", "커널");
            var draft = new GameObject("Draft", typeof(RectTransform), typeof(InputField)); draft.transform.SetParent(kernel.transform, false); draft.GetComponent<InputField>().SetTextWithoutNotify("save evidence");
            var panel = new GameObject("Notification", typeof(RectTransform)); panel.transform.SetParent(root.transform, false);
            try
            {
                KernelRuntime runtime = ClinicScenario.Create(); runtime.RegisterMail(new StoryMail { id = "new", subject = "임무", mailbox = Mailbox.Kernel });
                MonitorPageHost host = Monitor(root, runtime, kernel.GetComponent<MonitorPage>()); host.ShowPage("kernel");
                TerminalMailNotice notice = panel.AddComponent<TerminalMailNotice>(); notice.Initialize(host, 0.55f);
                int acknowledged = 0; notice.Acknowledged += () => acknowledged++;
                Assert.That(notice.Show(1), Is.True); Assert.That(host.SystemMenuVisible, Is.True); Assert.That(kernel.activeSelf, Is.False);
                Assert.That(panel.GetComponentsInChildren<Canvas>(), Is.Empty, "The envelope must use the existing physical CRT canvas.");
                Assert.That(panel.GetComponentInChildren<MailEnvelopeGraphic>().GetComponent<CrtMeshEffect>(), Is.Not.Null);
                Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel"));
                object menu = new object(); host.SetOverlayVisible(menu, true); notice.Hide();
                Assert.That(host.SystemMenuVisible, Is.True); Assert.That(kernel.activeSelf, Is.False); Assert.That(notice.Show(1), Is.False);
                host.SetOverlayVisible(menu, false); Assert.That(notice.Show(1), Is.True);
                panel.GetComponent<Button>().onClick.Invoke();
                Assert.That(acknowledged, Is.EqualTo(1)); Assert.That(notice.IsVisible, Is.False); Assert.That(host.SystemMenuVisible, Is.False);
                Assert.That(kernel.activeSelf, Is.True); Assert.That(draft.GetComponent<InputField>().text, Is.EqualTo("save evidence"));
                Assert.That(runtime.IsMailRead("new"), Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void BackgroundMailboxDoesNotAutomaticallyReadIncomingMail()
        {
            var root = new GameObject("Monitor", typeof(RectTransform)); root.SetActive(false);
            var mail = new GameObject("Mail", typeof(RectTransform), typeof(MonitorPage)); mail.GetComponent<MonitorPage>().Configure("mail", "메일");
            mail.AddComponent<MailMonitorPage>();
            try
            {
                KernelRuntime runtime = ClinicScenario.Create(); runtime.RegisterMail(new StoryMail { id = "new", subject = "임무", mailbox = Mailbox.Kernel });
                MonitorPageHost host = Monitor(root, runtime, mail.GetComponent<MonitorPage>()); host.AutoReadMailAllowed = () => false;
                host.ShowPage("mail"); root.SetActive(true);
                Assert.That(runtime.IsMailRead("new"), Is.False);
                host.AutoReadMailAllowed = () => true;
                typeof(MailMonitorPage).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(mail.GetComponent<MailMonitorPage>(), new object[] { true });
                Assert.That(runtime.IsMailRead("new"), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
#endif
