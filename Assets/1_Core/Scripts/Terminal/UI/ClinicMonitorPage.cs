using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private Text status, body, feedback;
        private MonitorNoteInput noteInput;
        private string profileId = "client";
        private readonly Dictionary<string, string> drafts = new Dictionary<string, string>(StringComparer.Ordinal);
        private float nextRefresh;
        public void Initialize(KernelRuntime value, Font font)
        {
            runtime = value; host = GetComponentInParent<MonitorPageHost>();
            if (body == null) Build(font);
            SelectProfile(host.SelectedProfileId);
            Refresh();
        }
        private void Update()
        {
            if (runtime == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f; Refresh();
        }
        private void OnEnable() { if (runtime != null && body != null) { SelectProfile(host.SelectedProfileId); Refresh(); } }
        private void Build(Font font)
        {
            status = MonitorWidgets.Label(transform, font, new Vector2(28, -90), new Vector2(770, 42), "", 28);
            MonitorWidgets.Button(transform, font, new Vector2(838, -90), new Vector2(120, 40), "이전 대상", () => ChangeProfile(-1));
            MonitorWidgets.Button(transform, font, new Vector2(972, -90), new Vector2(120, 40), "다음 대상", () => ChangeProfile(1));
            body = MonitorWidgets.ScrollText(transform, font, new Vector2(28, -146), new Vector2(1064, recordsPage ? 214 : 410));
            if (recordsPage)
            {
                noteInput = MonitorWidgets.Input(transform, font, new Vector2(28, -378), new Vector2(1064, 172));
                noteInput.onValueChanged.AddListener(value => drafts[profileId] = value);
                MonitorWidgets.Button(transform, font, new Vector2(28, -578), new Vector2(330, 46), "메모 저장", SaveNote);
                MonitorWidgets.Button(transform, font, new Vector2(394, -578), new Vector2(330, 46), "입력 비우기", () => noteInput.text = "");
                MonitorWidgets.Button(transform, font, new Vector2(762, -578), new Vector2(330, 46), "프로필 보기", () => host.ShowPage(consultationPageId));
                feedback = MonitorWidgets.Label(transform, font, new Vector2(28, -644), new Vector2(1064, 36), "직접 작성한 메모를 저장하세요.", 22);
            }
            else
            {
                MonitorWidgets.Button(transform, font, new Vector2(28, -588), new Vector2(1064, 48), "상담 기록 작성 / 조회", () => host.ShowPage(recordsPageId));
                feedback = MonitorWidgets.Label(transform, font, new Vector2(28, -650), new Vector2(1064, 34), "말풍선에서 선택한 단서와 저장한 메모를 확인할 수 있습니다.", 22);
            }
        }
        private void ChangeProfile(int direction)
        {
            string[] profiles = runtime.ConsultationProfiles.ToArray(); if (profiles.Length == 0) return;
            int current = Math.Max(0, Array.IndexOf(profiles, profileId));
            host.SelectedProfileId = profiles[(current + direction + profiles.Length) % profiles.Length];
            SelectProfile(host.SelectedProfileId);
            Refresh();
        }
        private void SelectProfile(string id)
        {
            if (profileId == id) return;
            if (noteInput != null) { noteInput.ReleaseFocus(); drafts[profileId] = noteInput.text; }
            profileId = id;
            if (noteInput != null) noteInput.SetTextWithoutNotify(drafts.TryGetValue(profileId, out string text) ? text : "");
        }
        private void Refresh()
        {
            status.text = (runtime.Entity(profileId)?.Label ?? profileId) + (recordsPage ? " / 직접 남긴 상담 기록" : " / 환자 프로필");
            if (!recordsPage) { body.text = runtime.ProfileSummary(profileId); return; }
            var text = new StringBuilder();
            foreach (ConsultationNote note in runtime.NotesFor(profileId)) text.Append("[메모 ").Append(note.id).Append(" / DAY ").Append(note.day)
                .Append("]\n").Append(note.text).Append("\n\n");
            body.text = text.Length == 0 ? "저장한 메모가 없습니다. 아래에 상담 내용을 직접 작성하세요." : text.ToString();
        }
        private void SaveNote()
        {
            try
            {
                noteInput.ReleaseFocus(); runtime.AddConsultationNote(profileId, noteInput.text);
                noteInput.SetTextWithoutNotify(""); drafts[profileId] = ""; feedback.text = "상담 메모를 저장했습니다."; Refresh();
            }
            catch (CommandException error) { feedback.text = error.Message; }
        }
    }
}
