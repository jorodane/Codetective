using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonitorPage))]
    public sealed class ClinicMonitorPage : MonoBehaviour, IMonitorPageContent
    {
        [SerializeField] private bool recordsPage;
        [SerializeField] private string consultationPageId = "clinic";
        [SerializeField] private string recordsPageId = "clinic-records";
        private KernelRuntime runtime;
        private MonitorPageHost host;
        private Font font;
        private RectTransform profiles, detailPane;
        private Text emptyProfiles;
        private Text status, body, feedback, pageLabel;
        private MonitorNoteInput noteInput, dayInput, startInput, endInput;
        private ProfileFormView form;
        private Button previousPage, nextPage;
        private string profileId = "client";
        private int currentPage = -1, selectedNoteId = -1;
        private float nextRefresh;
        private bool freshDraft;
        private string listSignature;
        private const float ListWidth = 230, ListHeight = 506, RowHeight = 52, PaneWidth = 806;
        private sealed class JournalDraft { public string text, day, start, end; }
        private readonly Dictionary<string, JournalDraft> drafts = new Dictionary<string, JournalDraft>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> profileChoices = new Dictionary<string, Button>(StringComparer.Ordinal);

        public void Initialize(KernelRuntime value, Font font)
        {
            CaptureDraft(); runtime = value; host = GetComponentInParent<MonitorPageHost>(); this.font = font;
            if (body == null) Build();
            currentPage = -1; selectedNoteId = -1; profileId = host.SelectedProfileId;
            Refresh();
        }
        private void OnEnable() { if (runtime != null && body != null) Refresh(); }
        private void OnDisable() => CaptureDraft();
        private void Update()
        {
            if (runtime == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f; Refresh();
        }
        private void Build()
        {
            MonitorWidgets.Label(transform, font, new Vector2(28, -88), new Vector2(ListWidth, 36), "인물 목록", 24);
            profiles = MonitorWidgets.ScrollList("ProfileList", "People", transform, new Vector2(28, -142), new Vector2(ListWidth, ListHeight));
            emptyProfiles = MonitorWidgets.Label(transform, font, new Vector2(286, -88), new Vector2(PaneWidth, 38), "열람할 수 있는 프로필이 없습니다.", 24);
            detailPane = MonitorWidgets.Rect("ProfileDetails", transform, new Vector2(286, -88), new Vector2(PaneWidth, 600));
            status = MonitorWidgets.Label(detailPane, font, Vector2.zero, new Vector2(PaneWidth, 38), "", 26);
            form = ProfileFormView.CreateHeader(detailPane, font, PaneWidth, true);
            ((RectTransform)form.transform).anchoredPosition = new Vector2(0, -50);
            MonitorWidgets.Label(detailPane, font, new Vector2(0, -242), new Vector2(324, 36), "상담 일지", 26);
            pageLabel = MonitorWidgets.Label(detailPane, font, new Vector2(350, -242), new Vector2(166, 36), "", 20);
            previousPage = MonitorWidgets.Button(detailPane, font, new Vector2(522, -242), new Vector2(132, 36), "이전 페이지", () => ChangePage(-1));
            nextPage = MonitorWidgets.Button(detailPane, font, new Vector2(664, -242), new Vector2(142, 36), "다음 페이지", () => ChangePage(1));
            if (recordsPage)
            {
                MonitorWidgets.Label(detailPane, font, new Vector2(0, -298), new Vector2(144, 32), "상담일 DAY", 22);
                dayInput = TimeInput(new Vector2(152, -294), 80); dayInput.characterLimit = 8;
                MonitorWidgets.Label(detailPane, font, new Vector2(258, -298), new Vector2(116, 32), "상담 시간", 22);
                startInput = TimeInput(new Vector2(382, -294), 110);
                MonitorWidgets.Label(detailPane, font, new Vector2(506, -298), new Vector2(24, 32), "~", 22);
                endInput = TimeInput(new Vector2(538, -294), 110);
                MonitorWidgets.Label(detailPane, font, new Vector2(0, -338), new Vector2(PaneWidth, 28), "상담 내용", 22);
                noteInput = MonitorWidgets.Input(detailPane, font, new Vector2(0, -368), new Vector2(PaneWidth, 138));
                foreach (MonitorNoteInput input in new[] { noteInput, dayInput, startInput, endInput }) input.onValueChanged.AddListener(_ => freshDraft = false);
                body = noteInput.textComponent;
                MonitorWidgets.Button(detailPane, font, new Vector2(0, -522), new Vector2(258, 38), "일지 저장", SaveNote);
                MonitorWidgets.Button(detailPane, font, new Vector2(274, -522), new Vector2(258, 38), "새 페이지", NewPage);
                MonitorWidgets.Button(detailPane, font, new Vector2(548, -522), new Vector2(258, 38), "프로필 보기", () => host.ShowPage(consultationPageId));
            }
            else
            {
                body = MonitorWidgets.ScrollText(detailPane, font, new Vector2(0, -294), new Vector2(PaneWidth, 212));
                MonitorWidgets.Button(detailPane, font, new Vector2(0, -522), new Vector2(PaneWidth, 38), "상담 일지 작성 / 수정", () => host.ShowPage(recordsPageId));
            }
            feedback = MonitorWidgets.Label(detailPane, font, new Vector2(0, -568), new Vector2(PaneWidth, 32), "", 20);
        }
        private MonitorNoteInput TimeInput(Vector2 position, float width)
        {
            MonitorNoteInput input = MonitorWidgets.Input(detailPane, font, position, new Vector2(width, 36));
            input.lineType = InputField.LineType.SingleLine; input.characterLimit = 5; return input;
        }
        private void SelectProfile(string id)
        {
            if (!isActiveAndEnabled || !(GetComponentInParent<TerminalController>()?.InteractionEnabled ?? true) ||
                !runtime.VisibleProfileIds.Contains(id)) return;
            host.SelectedProfileId = id; Refresh();
        }
        private void ChangePage(int direction)
        {
            int count = runtime.NotesFor(profileId).Count();
            host.SelectJournalPage(profileId, Math.Max(0, Math.Min(Math.Max(0, count - 1), currentPage + direction))); Refresh();
        }
        private void NewPage()
        { host.SelectJournalPage(profileId, runtime.NotesFor(profileId).Count()); Refresh(); }
        private void Refresh()
        {
            var people = runtime.VisibleProfileIds.Select(runtime.ProfileCard)
                .Select(profile => (id: profile.Id, name: ProfileFormView.ListName(profile)))
                .OrderBy(profile => profile.id == "kernel" ? 0 : 1).ThenBy(profile => profile.name, StringComparer.Ordinal).ToArray();
            string id = host.SelectedProfileId;
            if (!people.Any(profile => profile.id == id)) id = people.FirstOrDefault().id;
            RefreshProfileList(people, id);
            if (profileId != id) { CommitAndCapture(); profileId = id; currentPage = -1; selectedNoteId = -1; }
            host.SelectedProfileId = id;
            detailPane.gameObject.SetActive(id != null); emptyProfiles.gameObject.SetActive(id == null);
            if (id == null) return;
            PersonProfile card = runtime.ProfileCard(profileId); form.SetProfile(card, host.PortraitFor(profileId));
            status.text = card.DisplayName + (recordsPage ? " / 상담 일지 작성" : " / 인물 프로필");
            ConsultationNote[] entries = runtime.NotesFor(profileId).ToArray();
            int index = Math.Min(host.JournalPageFor(profileId), entries.Length);
            int noteId = index < entries.Length ? entries[index].id : 0;
            bool pageChanged = currentPage != index || selectedNoteId != noteId;
            if (pageChanged) LoadPage(index, entries);
            if (recordsPage && selectedNoteId == 0 && freshDraft && !new[] { noteInput, dayInput, startInput, endInput }.Any(input => input.isFocused)) ApplyDraft(DefaultDraft());
            pageLabel.text = entries.Length > 0 && index == entries.Length ? "새 페이지 / " + (index + 1) : (index + 1) + " / " + Math.Max(1, entries.Length) + " 페이지";
            previousPage.interactable = index > 0; nextPage.interactable = index + 1 < entries.Length;
            if (!recordsPage)
            {
                body.text = runtime.ProfileJournalText(profileId, index);
                if (pageChanged) { Canvas.ForceUpdateCanvases(); body.transform.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 1; }
            }
        }
        private void RefreshProfileList((string id, string name)[] people, string selected)
        {
            string signature = string.Join("\n", people.Select(person => person.id + ":" + person.name));
            if (listSignature != signature)
            {
                listSignature = signature;
                MonitorWidgets.ClearRows(profiles); profileChoices.Clear();
                for (int i = 0; i < people.Length; i++)
                {
                    var person = people[i];
                    Button button = MonitorWidgets.Button(profiles, font, new Vector2(0, -i * RowHeight),
                        new Vector2(ListWidth, RowHeight - 6), person.name, () => SelectProfile(person.id));
                    button.name = "Profile_" + person.id;
                    button.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                    profileChoices.Add(person.id, button);
                }
                profiles.sizeDelta = new Vector2(ListWidth, Mathf.Max(ListHeight, people.Length * RowHeight));
            }
            foreach (var entry in profileChoices)
                entry.Value.image.color = entry.Key == selected ? MonitorWidgets.SelectedButtonColor : MonitorWidgets.ButtonColor;
        }
        private string DraftKey => profileId + ":" + selectedNoteId;
        private void LoadPage(int index, ConsultationNote[] entries)
        {
            CommitAndCapture(); currentPage = index;
            ConsultationNote note = index < entries.Length ? entries[index] : null; selectedNoteId = note?.id ?? 0;
            if (!recordsPage) return;
            if (!drafts.TryGetValue(DraftKey, out JournalDraft draft))
            {
                draft = note == null ? DefaultDraft() : new JournalDraft { text = note.text, day = note.sessionDay.ToString(),
                    start = KernelRuntime.TimeOfDay(note.startMinute), end = KernelRuntime.TimeOfDay(note.endMinute) };
                freshDraft = note == null;
            }
            else freshDraft = false;
            ApplyDraft(draft);
            feedback.text = selectedNoteId == 0 ? "새 상담 내용을 직접 작성하고 저장하세요." : "저장한 상담일과 내용도 수정할 수 있습니다.";
        }
        private JournalDraft DefaultDraft()
        {
            var session = runtime.ConsultationNoteTime(profileId);
            return new JournalDraft { text = "", day = session.day.ToString(), start = KernelRuntime.TimeOfDay(session.startMinute), end = KernelRuntime.TimeOfDay(session.endMinute) };
        }
        private void ApplyDraft(JournalDraft draft)
        {
            noteInput.SetTextWithoutNotify(draft.text); dayInput.SetTextWithoutNotify(draft.day);
            startInput.SetTextWithoutNotify(draft.start); endInput.SetTextWithoutNotify(draft.end);
        }
        private void CommitAndCapture()
        {
            if (noteInput == null) return;
            noteInput.ReleaseFocus(); dayInput.ReleaseFocus(); startInput.ReleaseFocus(); endInput.ReleaseFocus(); CaptureDraft();
        }
        private void CaptureDraft()
        {
            if (noteInput == null || selectedNoteId < 0 || profileId == null) return;
            if (selectedNoteId == 0 && freshDraft) { drafts.Remove(DraftKey); return; }
            drafts[DraftKey] = new JournalDraft { text = noteInput.text, day = dayInput.text, start = startInput.text, end = endInput.text };
        }
        private void SaveNote()
        {
            try
            {
                CommitAndCapture();
                if (!int.TryParse(dayInput.text, out int day)) throw new CommandException("상담일 DAY를 숫자로 입력하세요.");
                int start = KernelRuntime.ParseConsultationTime(startInput.text), end = KernelRuntime.ParseConsultationTime(endInput.text);
                int noteId = selectedNoteId;
                if (noteId > 0) runtime.UpdateConsultationNote(noteId, noteInput.text, day, start, end);
                else noteId = runtime.AddConsultationNote(profileId, noteInput.text, day, start, end).id;
                drafts.Remove(DraftKey);
                ConsultationNote[] entries = runtime.NotesFor(profileId).ToArray();
                int index = Array.FindIndex(entries, n => n.id == noteId); host.SelectJournalPage(profileId, index);
                selectedNoteId = -1; LoadPage(index, entries); Refresh(); feedback.text = "상담 일지를 저장했습니다.";
            }
            catch (CommandException error) { feedback.text = error.Message; }
        }
    }
}
