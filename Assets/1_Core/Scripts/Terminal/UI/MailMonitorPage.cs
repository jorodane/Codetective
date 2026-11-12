using System;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonitorPage))]
    public sealed class MailMonitorPage : MonoBehaviour, IMonitorPageContent
    {
        [SerializeField] private Mailbox initialMailbox = Mailbox.Kernel;
        private KernelRuntime runtime;
        private Font font;
        private Mailbox mailbox;
        private string selectedId;
        private RectTransform list;
        private Text detail, title, feedback;
        private Button clinicTab, kernelTab, accept;
        private string listSignature;
        private float nextRefresh;
        public void Initialize(KernelRuntime value, Font font)
        {
            runtime = value; this.font = font;
            if (detail == null) { mailbox = initialMailbox; Build(); }
            Refresh(gameObject.activeInHierarchy);
        }
        private void OnEnable() { if (runtime != null && detail != null) Refresh(true); }
        private void Update()
        {
            if (runtime == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f; Refresh(true);
        }
        private void Build()
        {
            clinicTab = MonitorWidgets.Button(transform, font, new Vector2(28, -90), new Vector2(156, 44), "상담소 메일", () => ChangeMailbox(Mailbox.Clinic));
            kernelTab = MonitorWidgets.Button(transform, font, new Vector2(196, -90), new Vector2(156, 44), "Kernel 메일", () => ChangeMailbox(Mailbox.Kernel));
            title = MonitorWidgets.Label(transform, font, new Vector2(382, -90), new Vector2(710, 48), "", 26);
            RectTransform viewport = MonitorWidgets.Rect("Inbox", transform, new Vector2(28, -154), new Vector2(324, 466));
            viewport.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.03f);
            viewport.gameObject.AddComponent<CrtMeshEffect>(); viewport.gameObject.AddComponent<RectMask2D>();
            list = MonitorWidgets.Rect("Messages", viewport, Vector2.zero, new Vector2(324, 466));
            var scroll = viewport.gameObject.AddComponent<CrtScrollRect>(); scroll.content = list; scroll.viewport = viewport;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            detail = MonitorWidgets.ScrollText(transform, font, new Vector2(382, -154), new Vector2(710, 404));
            accept = MonitorWidgets.Button(transform, font, new Vector2(382, -578), new Vector2(710, 46), "확인", Accept);
            feedback = MonitorWidgets.Label(transform, font, new Vector2(28, -648), new Vector2(1064, 36), "메일을 선택해 내용을 확인하세요.", 22);
        }
        private void ChangeMailbox(Mailbox value) { mailbox = value; selectedId = null; listSignature = null; Refresh(true); }
        private void Refresh(bool openFirst)
        {
            StoryMail[] messages = runtime.Inbox(mailbox).ToArray();
            if (selectedId != null && !messages.Any(m => m.id == selectedId)) selectedId = null;
            if (selectedId == null && messages.Length > 0) selectedId = messages[0].id;
            TerminalController terminal = GetComponentInParent<TerminalController>();
            if (openFirst && selectedId != null && (terminal == null || terminal.InteractionEnabled)) runtime.ReadMail(selectedId);
            string signature = string.Join("|", messages.Select(m => m.id + runtime.IsMailRead(m.id) + runtime.IsMailAccepted(m.id))) + selectedId;
            if (signature != listSignature)
            {
                listSignature = signature;
                foreach (Transform child in list) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                for (int i = 0; i < messages.Length; i++)
                {
                    StoryMail message = messages[i];
                    Button button = MonitorWidgets.Button(list, font, new Vector2(0, -i * 90), new Vector2(324, 82),
                        (runtime.IsMailRead(message.id) ? "" : "● ") + message.subject, () => { selectedId = message.id; runtime.ReadMail(selectedId); Refresh(false); });
                    button.GetComponentInChildren<Text>().fontSize = 21;
                    if (message.id == selectedId) button.image.color = new Color(0.16f, 0.32f, 0.23f);
                }
                list.sizeDelta = new Vector2(324, Math.Max(466, messages.Length * 90));
            }
            clinicTab.image.color = mailbox == Mailbox.Clinic ? new Color(0.16f, 0.32f, 0.23f) : MonitorWidgets.ButtonColor;
            kernelTab.image.color = mailbox == Mailbox.Kernel ? new Color(0.16f, 0.32f, 0.23f) : MonitorWidgets.ButtonColor;
            StoryMail selected = messages.FirstOrDefault(m => m.id == selectedId);
            title.text = selected?.subject ?? (mailbox == Mailbox.Kernel ? "Kernel 메일함" : "상담소 메일함");
            detail.text = selected == null ? "도착한 메일이 없습니다." : "보낸 사람: " + selected.sender + "\nDAY " + selected.day + "\n\n" + selected.body;
            accept.gameObject.SetActive(selected != null && selected.requiresAcceptance);
            if (selected != null)
            {
                accept.interactable = !runtime.IsMailAccepted(selected.id);
                accept.GetComponentInChildren<Text>().text = runtime.IsMailAccepted(selected.id) ? "수락 완료" : selected.acceptLabel;
            }
        }
        private void Accept()
        {
            try { runtime.AcceptMail(selectedId); feedback.text = "업무를 수락했습니다. 다음 내용을 확인하세요."; Refresh(false); }
            catch (CommandException error) { feedback.text = error.Message; }
        }
    }
}
