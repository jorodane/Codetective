using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonitorPage))]
    public sealed class EvidenceMonitorPage : MonoBehaviour, IMonitorPageContent
    {
        private KernelRuntime runtime;
        private MonitorPageHost host;
        private TerminalController terminal;
        private Font font;
        private RectTransform rows;
        private Text status, detail, feedback;
        private MonitorNoteInput search, referenceName;
        private Button bind;
        private Button shortcut;
        private Image photo;
        private readonly List<Button> filters = new List<Button>();
        private readonly Dictionary<string, Button> entries = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> drafts = new Dictionary<string, string>(StringComparer.Ordinal);
        private string selectedId, signature;
        private int filter;
        private float nextRefresh;
        private bool Interactive => isActiveAndEnabled && host.CurrentPage == GetComponent<MonitorPage>() && terminal != null && terminal.InteractionEnabled;

        public void Initialize(KernelRuntime value, Font typeface)
        {
            runtime = value; font = typeface; host = GetComponentInParent<MonitorPageHost>(); terminal = host.GetComponent<TerminalController>();
            if (rows == null) Build();
            MonitorPage kernel = host.FindPage("kernel");
            if (shortcut == null && kernel != null)
                shortcut = MonitorWidgets.Button(kernel.transform, font, new Vector2(810, -132), new Vector2(282, 38), "확보 자료", () => host.ShowPage("evidence"));
            signature = null; Refresh();
        }
        private void OnEnable() { if (runtime != null && rows != null) Refresh(); }
        private void Update()
        { if (runtime != null && Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.2f; Refresh(); } }
        private void Build()
        {
            status = MonitorWidgets.Label(transform, font, new Vector2(28, -90), new Vector2(840, 44), "", 28);
            MonitorWidgets.Button(transform, font, new Vector2(900, -90), new Vector2(192, 44), "명령창으로", () => host.ShowPage("kernel"));
            string[] labels = { "전체", "도구", "문서·사진", "단서·진술", "기타" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                filters.Add(MonitorWidgets.Button(transform, font, new Vector2(28 + i * 148, -152), new Vector2(136, 40), labels[i], () => ChangeFilter(index)));
            }
            search = SingleLine(new Vector2(792, -152), new Vector2(300, 40), "자료 검색");
            search.onValueChanged.AddListener(_ => Refresh());
            RectTransform viewport = MonitorWidgets.Rect("EvidenceList", transform, new Vector2(28, -212), new Vector2(420, 382));
            Image background = viewport.gameObject.AddComponent<Image>(); background.color = new Color(0.02f, 0.04f, 0.03f);
            viewport.gameObject.AddComponent<CrtMeshEffect>(); viewport.gameObject.AddComponent<RectMask2D>();
            rows = MonitorWidgets.Rect("EvidenceRows", viewport, Vector2.zero, new Vector2(420, 382));
            CrtScrollRect scroll = viewport.gameObject.AddComponent<CrtScrollRect>(); scroll.content = rows; scroll.viewport = viewport;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            detail = MonitorWidgets.ScrollText(transform, font, new Vector2(472, -212), new Vector2(620, 382));
            photo = MonitorWidgets.Rect("EvidencePhoto", transform, new Vector2(472, -212), new Vector2(200, 250)).gameObject.AddComponent<Image>();
            photo.preserveAspect = true; photo.raycastTarget = false; photo.gameObject.AddComponent<CrtMeshEffect>(); photo.enabled = false;
            MonitorWidgets.Label(transform, font, new Vector2(472, -612), new Vector2(110, 36), "참조 이름", 22);
            referenceName = SingleLine(new Vector2(590, -608), new Vector2(240, 40), "예: 단서1"); referenceName.characterLimit = 64;
            referenceName.onValueChanged.AddListener(text => { if (selectedId != null) drafts[selectedId] = text; });
            bind = MonitorWidgets.Button(transform, font, new Vector2(846, -608), new Vector2(246, 40), "명령에 연결", BindSelected);
            feedback = MonitorWidgets.Label(transform, font, new Vector2(28, -656), new Vector2(1064, 32), "참조를 해제해도 확보한 자료는 보관됩니다.", 20);
        }
        private MonitorNoteInput SingleLine(Vector2 position, Vector2 size, string hint)
        {
            MonitorNoteInput input = MonitorWidgets.Input(transform, font, position, size); input.lineType = InputField.LineType.SingleLine;
            Text placeholder = MonitorWidgets.Label(input.transform, font, new Vector2(12, -8), size - new Vector2(24, 16), hint, 22);
            placeholder.color = new Color(0.36f, 0.55f, 0.43f); input.placeholder = placeholder; return input;
        }
        private void ChangeFilter(int value) { filter = value; signature = null; Refresh(); }
        private bool Matches(EvidenceEntry item)
        {
            DataKind kind = item.Value.kind;
            bool category = filter == 0 || filter == 1 && kind == DataKind.Item || filter == 2 && (kind == DataKind.Document || kind == DataKind.Photo || kind == DataKind.Log) ||
                filter == 3 && (kind == DataKind.Clue || kind == DataKind.Statement) || filter == 4 && kind != DataKind.Item && kind != DataKind.Document && kind != DataKind.Photo && kind != DataKind.Log && kind != DataKind.Clue && kind != DataKind.Statement;
            string query = search.text.Trim();
            return category && (query.Length == 0 || (item.Name + "\n" + item.Description + "\n" + string.Join(" ", item.References)).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        public void SelectEvidence(string id)
        {
            if (runtime == null || rows == null) return;
            EvidenceEntry item = runtime.Evidence.FirstOrDefault(e => e.Id == id); if (item == null) return;
            if (!Matches(item)) { filter = 0; search.SetTextWithoutNotify(""); signature = null; }
            Select(item); Refresh();
        }
        private void Select(EvidenceEntry item)
        {
            if (selectedId == item?.Id) return;
            referenceName.ReleaseFocus();
            if (selectedId != null) drafts[selectedId] = referenceName.text;
            selectedId = item?.Id;
            referenceName.SetTextWithoutNotify(item == null ? "" : drafts.TryGetValue(item.Id, out string draft) ? draft : runtime.SuggestEvidenceReference(item.Id));
            feedback.text = "";
            Canvas.ForceUpdateCanvases(); detail.transform.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 1;
        }
        private void Refresh()
        {
            IReadOnlyList<EvidenceEntry> all = runtime.Evidence; EvidenceEntry[] shown = all.Where(Matches).ToArray();
            if (!shown.Any(e => e.Id == selectedId)) Select(shown.FirstOrDefault());
            status.text = "확보 자료 / " + all.Count + "건" + (shown.Length != all.Count ? " · 표시 " + shown.Length + "건" : "");
            string next = string.Join("|", shown.Select(e => e.Number + ":" + e.Id + ":" + e.Name + ":" + runtime.TypeName(e.Value)));
            if (signature != next)
            {
                signature = next;
                for (int i = rows.childCount - 1; i >= 0; i--)
                { GameObject child = rows.GetChild(i).gameObject; child.SetActive(false); if (Application.isPlaying) Destroy(child); else DestroyImmediate(child); }
                entries.Clear();
                for (int i = 0; i < shown.Length; i++)
                {
                    EvidenceEntry item = shown[i];
                    entries[item.Id] = MonitorWidgets.Button(rows, font, new Vector2(0, -i * 84), new Vector2(420, 76), item.Name + "\n" + runtime.TypeName(item.Value),
                        () => { if (Interactive) { Select(item); Refresh(); } });
                }
                rows.sizeDelta = new Vector2(420, Math.Max(382, shown.Length * 84));
            }
            foreach (var row in entries) { row.Value.image.color = row.Key == selectedId ? new Color(0.16f, 0.32f, 0.23f) : MonitorWidgets.ButtonColor; row.Value.interactable = Interactive; }
            for (int i = 0; i < filters.Count; i++) filters[i].image.color = i == filter ? new Color(0.16f, 0.32f, 0.23f) : MonitorWidgets.ButtonColor;
            EvidenceEntry selected = shown.FirstOrDefault(e => e.Id == selectedId);
            Sprite image = selected != null ? host.EvidencePhotoFor(selected.Id) : null;
            photo.sprite = image; photo.enabled = image != null;
            RectTransform descriptionArea = (RectTransform)detail.transform.parent;
            descriptionArea.anchoredPosition = new Vector2(image != null ? 690 : 472, -212);
            descriptionArea.sizeDelta = new Vector2(image != null ? 402 : 620, 382);
            bind.interactable = referenceName.interactable = selected != null && Interactive;
            detail.text = selected == null ? (all.Count == 0 ? "아직 확보한 자료가 없습니다.\n\n상담 단서, 신문 기사, 조사 결과와 전달받은 도구가 여기에 등록됩니다." : "조건에 맞는 자료가 없습니다.") :
                selected.Name + "\n\n분류: " + runtime.TypeName(selected.Value) + "\n확보: " + (selected.HasAcquiredTime ? "DAY " + selected.Day + " / " + KernelRuntime.TimeOfDay(selected.Minute) : "이전 기록 · 시각 미기록") +
                "\n출처: " + selected.Source + "\n\n" + selected.Description + "\n\n명령용 참조: " + (selected.References.Length == 0 ? "없음" : string.Join(", ", selected.References));
        }
        private void BindSelected()
        {
            if (!Interactive || selectedId == null) return;
            try
            {
                referenceName.ReleaseFocus(); runtime.ReferenceEvidence(selectedId, referenceName.text); Refresh();
                feedback.text = "'" + referenceName.text + "' 참조를 연결했습니다. 명령창에서 사용할 수 있습니다.";
            }
            catch (CommandException error) { feedback.text = error.Message; }
        }
    }
}
