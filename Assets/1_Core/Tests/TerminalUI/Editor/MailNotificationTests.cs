#if UNITY_EDITOR
using System.Collections.Generic;
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
        private static void Invoke(MonitorPageHost host, string method) =>
            typeof(MonitorPageHost).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(host, null);
        private static MonitorPageHost Monitor(GameObject root, KernelRuntime runtime, params MonitorPage[] pages)
        {
            var navigation = new GameObject("Navigation", typeof(RectTransform)); navigation.transform.SetParent(root.transform, false);
            navigation.GetComponent<RectTransform>().sizeDelta = new Vector2(1090, 44);
            var heading = new GameObject("Heading", typeof(RectTransform), typeof(Text));
            heading.transform.SetParent(root.transform, false);
            var rule = new GameObject("HeaderRule", typeof(RectTransform), typeof(Image));
            rule.transform.SetParent(root.transform, false);
            foreach (MonitorPage page in pages) page.transform.SetParent(root.transform, false);
            MonitorPageHost host = root.AddComponent<MonitorPageHost>(); Set(host, "navigationRoot", navigation.GetComponent<RectTransform>());
            Set(host, "heading", heading.GetComponent<Text>());
            Set(host, "headerRule", rule.GetComponent<Image>());
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
                Assert.That(panel.GetComponentsInChildren<Canvas>(true), Is.Empty, "The envelope must use the existing physical CRT canvas.");
                Assert.That(panel.GetComponentsInChildren<Text>(true), Is.Empty, "The notice must be readable from the overview without text.");
                Image display = panel.GetComponent<Image>();
                Assert.That(display.sprite, Is.Not.Null, "The full screen notice must load its sprite from Resources.");
                Assert.That(display.sprite.name, Is.EqualTo("MailNoticeScreen"));
                Assert.That(display.mainTexture, Is.EqualTo(display.sprite.texture));
                Assert.That(display.GetComponent<CrtMeshEffect>(), Is.Not.Null);
                Assert.That(panel.GetComponentsInChildren<Graphic>(true), Is.EqualTo(new Graphic[] { display }));
                Assert.That(root.transform.Find("Heading").GetComponent<Text>().enabled, Is.False);
                Assert.That(root.transform.Find("HeaderRule").GetComponent<Image>().enabled, Is.False);
                Assert.That(root.transform.Find("Navigation").gameObject.activeSelf, Is.False);
                var cover = panel.GetComponent<RectTransform>();
                Assert.That(cover.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(cover.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(panel.GetComponent<Image>().color.a, Is.EqualTo(1f));
                Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel"));
                object menu = new object(); host.SetOverlayVisible(menu, true); notice.Hide();
                Assert.That(host.SystemMenuVisible, Is.True); Assert.That(kernel.activeSelf, Is.False); Assert.That(notice.Show(1), Is.False);
                host.SetOverlayVisible(menu, false); Assert.That(notice.Show(1), Is.True);
                panel.GetComponent<Button>().onClick.Invoke();
                Assert.That(acknowledged, Is.EqualTo(1)); Assert.That(notice.IsVisible, Is.False); Assert.That(host.SystemMenuVisible, Is.False);
                Assert.That(root.transform.Find("Heading").GetComponent<Text>().enabled, Is.True);
                Assert.That(root.transform.Find("HeaderRule").GetComponent<Image>().enabled, Is.True);
                Assert.That(root.transform.Find("Navigation").gameObject.activeSelf, Is.True);
                Assert.That(kernel.activeSelf, Is.True); Assert.That(draft.GetComponent<InputField>().text, Is.EqualTo("save evidence"));
                Assert.That(runtime.IsMailRead("new"), Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MailArrivalUsesTheSelectedColorThenOnlyTheUnreadMailTabPulses()
        {
            var root = new GameObject("Monitor", typeof(RectTransform));
            root.SetActive(false);
            var kernel = new GameObject("Kernel", typeof(RectTransform), typeof(MonitorPage)).GetComponent<MonitorPage>();
            var mail = new GameObject("Mail", typeof(RectTransform), typeof(MonitorPage)).GetComponent<MonitorPage>();
            kernel.Configure("kernel", "커널");
            mail.Configure("mail", "메일");
            try
            {
                var runtime = new KernelRuntime();
                MonitorPageHost host = Monitor(root, runtime, kernel, mail);
                host.ShowPage("kernel");
                var tabs = (Dictionary<string, Button>)typeof(MonitorPageHost)
                    .GetField("tabs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(host);
                Image bar = root.transform.Find("TerminalHeaderBackground").GetComponent<Image>();
                Color selected = tabs["kernel"].image.color;
                Color idle = tabs["mail"].image.color;

                runtime.RegisterMail(new StoryMail { id = "new", subject = "임무", mailbox = Mailbox.Kernel });
                Assert.That(host.ShowMailReceived(1, null), Is.True);
                Assert.That(bar.color, Is.EqualTo(selected));
                Assert.That(tabs["mail"].image.color, Is.EqualTo(selected));

                Set(host, "mailBannerUntil", Time.unscaledTime - 0.1f);
                Invoke(host, "RefreshHeader");
                Assert.That(bar.color, Is.EqualTo(Color.clear));
                Assert.That(tabs["kernel"].image.color, Is.EqualTo(selected));
                Assert.That(tabs["mail"].image.color, Is.Not.EqualTo(idle));

                Set(host, "mailBannerUntil", Time.unscaledTime - 0.65f);
                Invoke(host, "RefreshHeader");
                Assert.That(tabs["mail"].image.color, Is.EqualTo(idle));

                runtime.ReadMail("new");
                Set(host, "mailBannerUntil", Time.unscaledTime - 0.1f);
                Invoke(host, "RefreshUnreadMail");
                Invoke(host, "RefreshHeader");
                Assert.That(tabs["mail"].image.color, Is.EqualTo(idle));
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
