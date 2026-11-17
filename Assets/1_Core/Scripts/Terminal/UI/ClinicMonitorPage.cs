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
        private Text status, body, feedback, pageLabel;
        private MonitorNoteInput noteInput, dayInput, startInput, endInput;
        private ProfileFormView form;
        private Button previousPage, nextPage;
        private string profileId = "client";
        private int currentPage = -1, selectedNoteId = -1;
        private float nextRefresh;
        private bool freshDraft;
        private sealed class JournalDraft { public string text, day, start, end; }
        private readonly Dictionary<string, JournalDraft> drafts = new Dictionary<string, JournalDraft>(StringComparer.Ordinal);

        public void Initialize(KernelRuntime value, Font font)
        {
            CaptureDraft(); runtime = value; host = GetComponentInParent<MonitorPageHost>();
            if (body == null) Build(font);
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
        private void Build(Font font)
        {
            status = MonitorWidgets.Label(transform, font, new Vector2(28, -88), new Vector2(770, 36), "", 26);
            MonitorWidgets.Button(transform, font, new Vector2(838, -88), new Vector2(120, 36), "이전 대상", () => ChangeProfile(-1));
            MonitorWidgets.Button(transform, font, new Vector2(972, -88), new Vector2(120, 36), "다음 대상", () => ChangeProfile(1));
            form = ProfileFormView.CreateHeader(transform, font, 1064, true);
            ((RectTransform)form.transform).anchoredPosition = new Vector2(28, -138);
            MonitorWidgets.Label(transform, font, new Vector2(28, -374), new Vector2(500, 32), "상담 일지", 26);
            pageLabel = MonitorWidgets.Label(transform, font, new Vector2(560, -374), new Vector2(245, 32), "", 20);
            previousPage = MonitorWidgets.Button(transform, font, new Vector2(820, -374), new Vector2(124, 32), "이전 페이지", () => ChangePage(-1));
            nextPage = MonitorWidgets.Button(transform, font, new Vector2(960, -374), new Vector2(132, 32), "다음 페이지", () => ChangePage(1));
            if (recordsPage)
            {
                MonitorWidgets.Label(transform, font, new Vector2(28, -418), new Vector2(160, 32), "상담일 DAY", 22);
                dayInput = TimeInput(font, new Vector2(190, -414), 80); dayInput.characterLimit = 8;
                MonitorWidgets.Label(transform, font, new Vector2(296, -418), new Vector2(108, 32), "상담 시간", 22);
                startInput = TimeInput(font, new Vector2(410, -414), 110);
                MonitorWidgets.Label(transform, font, new Vector2(534, -418), new Vector2(24, 32), "~", 22);
                endInput = TimeInput(font, new Vector2(566, -414), 110);
                MonitorWidgets.Label(transform, font, new Vector2(28, -460), new Vector2(1064, 28), "상담 내용", 22);
                noteInput = MonitorWidgets.Input(transform, font, new Vector2(28, -490), new Vector2(1064, 104));
                foreach (MonitorNoteInput input in new[] { noteInput, dayInput, startInput, endInput }) input.onValueChanged.AddListener(_ => freshDraft = false);
                body = noteInput.textComponent;
                MonitorWidgets.Button(transform, font, new Vector2(28, -610), new Vector2(330, 38), "일지 저장", SaveNote);
                MonitorWidgets.Button(transform, font, new Vector2(394, -610), new Vector2(330, 38), "새 페이지", NewPage);
                MonitorWidgets.Button(transform, font, new Vector2(762, -610), new Vector2(330, 38), "프로필 보기", () => host.ShowPage(consultationPageId));
            }
            else
            {
                body = MonitorWidgets.ScrollText(transform, font, new Vector2(28, -418), new Vector2(1064, 178));
                MonitorWidgets.Button(transform, font, new Vector2(28, -610), new Vector2(1064, 38), "상담 일지 작성 / 수정", () => host.ShowPage(recordsPageId));
            }
            feedback = MonitorWidgets.Label(transform, font, new Vector2(28, -656), new Vector2(1064, 32), "", 20);
        }
        private MonitorNoteInput TimeInput(Font font, Vector2 position, float width)
        {
            MonitorNoteInput input = MonitorWidgets.Input(transform, font, position, new Vector2(width, 36));
            input.lineType = InputField.LineType.SingleLine; input.characterLimit = 5; return input;
        }
        private void ChangeProfile(int direction)
        {
            string[] profiles = runtime.VisibleProfileIds.ToArray(); if (profiles.Length == 0) return;
            int index = Math.Max(0, Array.IndexOf(profiles, profileId));
            host.SelectedProfileId = profiles[(index + direction + profiles.Length) % profiles.Length]; Refresh();
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
            string id = host.SelectedProfileId;
            if (!runtime.VisibleProfileIds.Contains(id)) id = runtime.VisibleProfileIds.FirstOrDefault();
            if (id == null) { status.text = "프로필"; body.text = "열람 가능한 프로필이 없습니다."; form.gameObject.SetActive(false); return; }
            host.SelectedProfileId = id;
            if (profileId != id) { CommitAndCapture(); profileId = id; currentPage = -1; selectedNoteId = -1; }
            PersonProfile profile = runtime.ProfileCard(profileId); form.gameObject.SetActive(true); form.SetProfile(profile, host.PortraitFor(profileId));
            status.text = profile.DisplayName + (recordsPage ? " / 상담 일지 작성" : " / 인물 프로필");
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
            int end = Math.Min(1439, (int)runtime.Minute); AppointmentProgress visit = runtime.CurrentAppointment;
            int start = visit != null && visit.profileId == profileId ? Math.Min(end, visit.startMinute) : end;
            return new JournalDraft { text = "", day = runtime.Day.ToString(), start = KernelRuntime.TimeOfDay(start), end = KernelRuntime.TimeOfDay(end) };
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
