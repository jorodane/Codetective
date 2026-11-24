using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonitorPage))]
    public sealed class MailMonitorPage : MonoBehaviour, IMonitorPageContent
    {
        private static readonly CultureInfo KoreanDateCulture = CultureInfo.GetCultureInfo("ko-KR");
        [SerializeField] private Mailbox initialMailbox = Mailbox.Kernel;
        private KernelRuntime runtime;
        private Font font;
        private Mailbox mailbox;
        private string selectedId, displayedId;
        private RectTransform list;
        private Text detail, sender, recipient, title, date, feedback;
        private ScrollRect bodyScroll;
        private Button clinicTab, kernelTab, accept;
        private string listSignature;
        private RectTransform choiceList;
        private readonly List<Button> choiceButtons = new List<Button>();
        private string choiceSignature;
        private float nextRefresh;
        private const float InboxWidth = 368, InboxHeight = 458, RowHeight = 76;
        private const float DateWidth = 100, ConfirmationWidth = 44, SubjectWidth = InboxWidth - DateWidth - ConfirmationWidth;
        private const float DetailLeft = 418, DetailWidth = 674, BodyTop = 252;
        private bool Interactive => gameObject.activeInHierarchy && (GetComponentInParent<TerminalController>()?.InteractionEnabled ?? true);
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
            RectTransform headings = MonitorWidgets.Rect("InboxHeadings", transform, new Vector2(28, -140), new Vector2(InboxWidth, 26));
            ListField(headings, 0, DateWidth, 26, "날짜", 20);
            ListField(headings, DateWidth, SubjectWidth, 26, "제목", 20);
            ListField(headings, DateWidth + SubjectWidth, ConfirmationWidth, 26, "확인", 16);
            list = MonitorWidgets.ScrollList("Inbox", "Messages", transform, new Vector2(28, -170), new Vector2(InboxWidth, InboxHeight));
            RectTransform header = MonitorWidgets.Rect("MailHeader", transform, new Vector2(DetailLeft, -90), new Vector2(DetailWidth, 150));
            Image background = header.gameObject.AddComponent<Image>();
            background.color = new Color(0.055f, 0.11f, 0.08f); background.raycastTarget = false;
            header.gameObject.AddComponent<CrtMeshEffect>();
            sender = HeaderField(header, "보낸 사람", -8, 24);
            recipient = HeaderField(header, "받는 사람", -40, 24);
            title = HeaderField(header, "제목", -72, 26);
            date = HeaderField(header, "날짜", -104, 24);
            detail = MonitorWidgets.ScrollText(transform, font, new Vector2(DetailLeft, -BodyTop), new Vector2(DetailWidth, 624 - BodyTop));
            bodyScroll = detail.transform.parent.GetComponent<ScrollRect>();
            accept = MonitorWidgets.Button(transform, font, new Vector2(DetailLeft, -578), new Vector2(DetailWidth, 46), "확인", Accept);
            choiceList = MonitorWidgets.ScrollList("MailChoices", "Responses", transform, new Vector2(DetailLeft, -480), new Vector2(DetailWidth, 144));
            feedback = MonitorWidgets.Label(transform, font, new Vector2(28, -648), new Vector2(1064, 36), "", 22);
        }
        private Text ListField(Transform row, float left, float width, float height, string value, int fontSize)
        {
            Text field = MonitorWidgets.Label(row, font, new Vector2(left + 5, 0), new Vector2(width - 10, height), value, fontSize);
            field.alignment = TextAnchor.MiddleCenter;
            return field;
        }

        private string DisplayDate(int day, bool showWeekday = false) =>
            runtime.DateAt(day, 0).Value.ToString(showWeekday ? "yy.MM.dd (ddd)" : "yy.MM.dd", KoreanDateCulture);

        private Text HeaderField(Transform header, string caption, float top, int fontSize)
        {
            Text label = MonitorWidgets.Label(header, font, new Vector2(12, top), new Vector2(122, 34), caption, 20);
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.Lerp(MonitorWidgets.Foreground, MonitorWidgets.ButtonColor, 0.25f);
            Text value = MonitorWidgets.Label(header, font, new Vector2(138, top), new Vector2(DetailWidth - 150, 34), "", fontSize);
            value.alignment = TextAnchor.MiddleLeft;
            value.resizeTextForBestFit = true; value.resizeTextMinSize = 16; value.resizeTextMaxSize = fontSize;
            return value;
        }
        private void ChangeMailbox(Mailbox value) { mailbox = value; selectedId = null; listSignature = null; feedback.text = ""; Refresh(true); }
        public void ShowMailbox(Mailbox value) => ChangeMailbox(value);
        public void ShowMessage(string id)
        {
            StoryMail message = runtime.Inbox(Mailbox.Clinic).Concat(runtime.Inbox(Mailbox.Kernel)).FirstOrDefault(m => m.id == id);
            if (message == null) return;
            mailbox = message.mailbox; selectedId = id; listSignature = null; feedback.text = ""; Refresh(Interactive);
        }
        private void Refresh(bool openFirst)
        {
            StoryMail[] messages = runtime.Inbox(mailbox).ToArray();
            if (selectedId != null && !messages.Any(m => m.id == selectedId)) selectedId = null;
            if (selectedId == null && messages.Length > 0) selectedId = messages[0].id;
            if (openFirst && selectedId != null && Interactive && (GetComponentInParent<MonitorPageHost>()?.AutoReadMailAllowed?.Invoke() ?? true)) runtime.ReadMail(selectedId);
            string signature = string.Join("|", messages.Select(m =>
                $"{m.id}:{DisplayDate(m.day)}:{runtime.IsMailRead(m.id)}")) + selectedId;
            if (signature != listSignature)
            {
                listSignature = signature;
                MonitorWidgets.ClearRows(list);
                for (int i = 0; i < messages.Length; i++) AddMessageRow(messages[i], i);
                list.sizeDelta = new Vector2(InboxWidth, Math.Max(InboxHeight, messages.Length * RowHeight));
            }
            clinicTab.image.color = mailbox == Mailbox.Clinic ? new Color(0.16f, 0.32f, 0.23f) : MonitorWidgets.ButtonColor;
            kernelTab.image.color = mailbox == Mailbox.Kernel ? new Color(0.16f, 0.32f, 0.23f) : MonitorWidgets.ButtonColor;
            StoryMail selected = messages.FirstOrDefault(m => m.id == selectedId);
            sender.text = string.IsNullOrWhiteSpace(selected?.sender) ? "—" : selected.sender;
            recipient.text = string.IsNullOrWhiteSpace(selected?.recipient) ? (mailbox == Mailbox.Kernel ? "Kernel" : "상담소") : selected.recipient;
            title.text = string.IsNullOrWhiteSpace(selected?.subject) ? "—" : selected.subject;
            date.text = selected != null ? DisplayDate(selected.day, showWeekday: true) : "—";
            string body = selected == null ? "도착한 메일이 없습니다." : selected.body ?? "";
            if (selected?.HasReservation == true)
            {
                ReservationRequest request = selected.reservation;
                body += "\n\n예약 문의: " + DisplayDate(request.day) + " / " + KernelRuntime.TimeOfDay(request.appointment.startMinute) +
                    " ~ " + KernelRuntime.TimeOfDay(request.appointment.BookingEndMinute) + "\n" + request.appointment.patientName;
            }
            if (selected != null && !string.IsNullOrEmpty(runtime.SelectedMailChoice(selected.id))) body += "\n\n답변: " + runtime.SelectedMailChoiceLabel(selected.id);
            detail.text = RefreshChoices(selected, body);
            if (displayedId != selectedId)
            {
                displayedId = selectedId;
                bodyScroll.StopMovement();
                detail.rectTransform.anchoredPosition = Vector2.zero;
            }
            accept.gameObject.SetActive(selected != null && selected.requiresAcceptance);
            if (selected != null)
            {
                accept.interactable = !runtime.IsMailAccepted(selected.id);
                accept.GetComponentInChildren<Text>(true).text = runtime.IsMailAccepted(selected.id) ? "수락 완료" : selected.acceptLabel;
            }
        }
        private void AddMessageRow(StoryMail message, int index)
        {
            const float height = RowHeight - 6;
            Button button = MonitorWidgets.Button(list, font, new Vector2(0, -index * RowHeight), new Vector2(InboxWidth, height), message.subject, () =>
            {
                if (!Interactive) return;
                selectedId = message.id; runtime.ReadMail(selectedId); feedback.text = ""; Refresh(false);
            });
            button.name = "Mail_" + message.id;
            if (message.id == selectedId) button.image.color = MonitorWidgets.SelectedButtonColor;

            Text subject = button.GetComponentInChildren<Text>(true);
            subject.name = "Subject";
            subject.rectTransform.anchoredPosition = new Vector2(DateWidth + 8, -4);
            subject.rectTransform.sizeDelta = new Vector2(SubjectWidth - 16, height - 8);
            subject.alignment = TextAnchor.MiddleLeft;
            subject.resizeTextForBestFit = true;
            subject.resizeTextMinSize = 16;
            subject.resizeTextMaxSize = 22;
            Text sentDate = ListField(button.transform, 0, DateWidth, height, DisplayDate(message.day), 20);
            sentDate.name = "Date";
            sentDate.resizeTextForBestFit = true;
            sentDate.resizeTextMinSize = 16; sentDate.resizeTextMaxSize = 20;

            if (runtime.IsMailRead(message.id)) AddReadCheck(button.transform, height);
        }

        private static void AddReadCheck(Transform row, float height)
        {
            Vector2 size = new Vector2(16, 14);
            var check = MonitorWidgets.Rect("ReadCheck", row,
                new Vector2(InboxWidth - (ConfirmationWidth + size.x) * 0.5f, (size.y - height) * 0.5f), size);
            // The terminal font does not contain a check mark glyph.
            DrawCheckStroke(check, new Vector2(1, -7), 7, -45);
            DrawCheckStroke(check, new Vector2(5.8f, -11.8f), 13, 49);
        }

        private static void DrawCheckStroke(Transform check, Vector2 start, float length, float angle)
        {
            var stroke = MonitorWidgets.Rect("Stroke", check, start, new Vector2(length, 2.5f));
            stroke.pivot = new Vector2(0, 0.5f);
            stroke.anchoredPosition = start;
            stroke.localRotation = Quaternion.Euler(0, 0, angle);
            var image = stroke.gameObject.AddComponent<Image>();
            image.color = MonitorWidgets.Foreground;
            image.raycastTarget = false;
            stroke.gameObject.AddComponent<CrtMeshEffect>();
        }
        private void Accept()
        {
            if (!Interactive) return;
            try
            {
                StoryMail message = runtime.Inbox(mailbox).FirstOrDefault(item => item.id == selectedId);
                runtime.AcceptMail(selectedId);
                if (!string.IsNullOrEmpty(message?.pageOnAccept) &&
                    (GetComponentInParent<MonitorPageHost>()?.TryNavigate(message.pageOnAccept) ?? false)) return;
                feedback.text = "업무를 수락했습니다. 다음 내용을 확인하세요.";
                Refresh(false);
            }
            catch (CommandException error) { feedback.text = error.Message; }
        }
        private string RefreshChoices(StoryMail selected, string body)
        {
            MailChoice[] choices = selected?.choices ?? Array.Empty<MailChoice>();
            choiceList.parent.gameObject.SetActive(choices.Length > 0);
            float bottom = choices.Length > 0 ? 466 : selected?.requiresAcceptance == true ? 564 : 624;
            bodyScroll.viewport.sizeDelta = new Vector2(DetailWidth, bottom - BodyTop);
            string signature = selected?.id + "|" + string.Join("|", choices.Select(c => c.id));
            if (choiceSignature != signature)
            {
                choiceSignature = signature;
                MonitorWidgets.ClearRows(choiceList);
                choiceButtons.Clear();
                for (int i = 0; i < choices.Length; i++)
                {
                    string choiceId = choices[i].id;
                    choiceButtons.Add(MonitorWidgets.Button(choiceList, font, new Vector2(0, -i * 48), new Vector2(DetailWidth, 42), choices[i].label, () => Choose(choiceId)));
                }
                choiceList.sizeDelta = new Vector2(DetailWidth, Math.Max(144, choices.Length * 48));
                choiceList.anchoredPosition = Vector2.zero;
            }
            for (int i = 0; i < choices.Length; i++)
            {
                string denial = runtime.MailChoiceDenial(selected.id, choices[i].id);
                choiceButtons[i].interactable = Interactive && denial == null;
                bool chosen = runtime.SelectedMailChoice(selected.id) == choices[i].id;
                choiceButtons[i].GetComponentInChildren<Text>(true).text = (chosen ? "✓ " : "") + choices[i].label + (chosen ? " [답변 완료]" : "");
                if (string.IsNullOrEmpty(runtime.SelectedMailChoice(selected.id)) && choices[i].approvesReservation && denial != null && runtime.IsMailRead(selected.id))
                    body += "\n\n승인 불가: " + denial;
            }
            return body;
        }
        private void Choose(string choiceId)
        {
            if (!Interactive) return;
            try
            {
                runtime.ChooseMail(selectedId, choiceId);
                StoryMail selected = runtime.Inbox(mailbox).First(m => m.id == selectedId);
                if (selected.HasReservation && runtime.IsMailAccepted(selectedId))
                {
                    MonitorPageHost host = GetComponentInParent<MonitorPageHost>();
                    if (host != null) host.SelectedScheduleDay = selected.reservation.day;
                    feedback.text = "예약을 승인했습니다. 스케줄에서 확인하세요.";
                }
                else feedback.text = "답변을 기록했습니다: " + runtime.SelectedMailChoiceLabel(selectedId);
                Refresh(false);
            }
            catch (CommandException error) { feedback.text = error.Message; }
        }
    }
}
