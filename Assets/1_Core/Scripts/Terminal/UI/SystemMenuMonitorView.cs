using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed class SystemSaveInfo
    {
        public bool Available;
        public int Day;
        public string Id, GameTime, Kind, Situation, SavedAt;
    }
    public sealed class SystemMenuActions
    {
        public Action Resume, NewGame, Save, Title, Quit;
        public Action<string> Load;
        public Func<IReadOnlyList<SystemSaveInfo>> ReadSaves;
        public Func<CommandLocale> GetLanguage;
        public Action<CommandLocale> SetLanguage;
    }

    /// <summary>A system screen on the existing CRT, separate from page history and command input.</summary>
    [DisallowMultipleComponent]
    public sealed class SystemMenuMonitorView : MonoBehaviour
    {
        private const string VolumeKey = "Codetective.MasterVolume", FullscreenKey = "Codetective.Fullscreen";
        private MonitorPageHost host;
        private SystemMenuActions actions;
        private RectTransform content;
        private Font font;
        private Text heading, footer, message;
        private string section = "main";
        private string selectedSaveId;
        private IReadOnlyList<SystemSaveInfo> saveSlots = Array.Empty<SystemSaveInfo>();
        private readonly Dictionary<string, Button> saveButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly List<Selectable> menuControls = new List<Selectable>();
        private readonly List<Button> settingsTabs = new List<Button>();
        private int settingsCategory;
        private bool savesRead;
        private Button loadButton;
        public bool IsTitle { get; private set; }
        public bool IsVisible => gameObject.activeSelf;

        public static void ApplySavedSettings()
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            if (PlayerPrefs.HasKey(FullscreenKey)) Screen.fullScreen = PlayerPrefs.GetInt(FullscreenKey) != 0;
        }
        public void Initialize(MonitorPageHost owner, SystemMenuActions callbacks)
        {
            host = owner; actions = callbacks ?? new SystemMenuActions(); font = host.DisplayFont;
            if (content != null) return;
            RectTransform root = (RectTransform)transform;
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            Image background = gameObject.AddComponent<Image>(); background.color = new Color(0.018f, 0.035f, 0.025f);
            gameObject.AddComponent<CrtMeshEffect>();
            heading = MonitorWidgets.Label(transform, font, new Vector2(64, -46), new Vector2(1000, 72), "", 44);
            content = MonitorWidgets.Rect("MenuContent", transform, new Vector2(64, -142), new Vector2(1000, 470));
            message = MonitorWidgets.Label(transform, font, new Vector2(64, -614), new Vector2(1000, 46), "", 21);
            footer = MonitorWidgets.Label(transform, font, new Vector2(64, -668), new Vector2(1000, 28), "", 19);
        }
        public void Show(bool title)
        {
            IsTitle = title; section = "main"; savesRead = false;
            host.SetOverlayVisible(this, true); transform.SetAsLastSibling(); gameObject.SetActive(true); Render();
        }
        public void Hide() { gameObject.SetActive(false); if (host != null) host.SetOverlayVisible(this, false); }
        private void OnDestroy() { if (host != null) host.SetOverlayVisible(this, false); }
        // Escape goes back within system screens; only the in-game root may resume gameplay.
        public bool HandleEscape()
        {
            if (section != "main") { Open("main"); return true; }
            return IsTitle;
        }
        public void ShowFeedback(string value) { if (message != null) message.text = value; }
        public void RefreshSaveList()
        {
            savesRead = false;
            if (isActiveAndEnabled && (section == "load" || (IsTitle && section == "main"))) Render();
        }
        private void Open(string value) { section = value; Render(); }
        private bool CanUse => isActiveAndEnabled && host.SystemMenuVisible && host.GetComponent<TerminalController>().InteractionEnabled;
        private void Run(Action action) { if (CanUse) action?.Invoke(); }
        private Button Option(int row, string label, Action action, bool enabled = true)
        {
            Button button = MonitorWidgets.Button(content, font, new Vector2(268, -row * 76), new Vector2(464, 58), label, () => { if (enabled) Run(action); });
            button.interactable = enabled; menuControls.Add(button); return button;
        }
        private void Render()
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject; child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            heading.text = section == "main" ? IsTitle ? "메인 메뉴" : "일시정지됨" : section == "load" ? "불러오기" : section == "guide" ? "가이드" : section == "quit" ? "정말 끝내시겠습니까?" : "설정";
            message.text = "";
            saveButtons.Clear(); menuControls.Clear(); settingsTabs.Clear(); loadButton = null;
            footer.text = section != "main" ? "ESC 돌아가기" : IsTitle ? "CODETECTIVE / KERNEL TERMINAL" : "ESC 계속하기";
            switch (section)
            {
                case "main":
                    if (IsTitle)
                    {
                        IReadOnlyList<SystemSaveInfo> saves = ReadSaves();
                        SystemSaveInfo latest = saves.FirstOrDefault(s => s.Available);
                        Option(0, "이어하기", () => actions.Load?.Invoke(latest.Id), latest != null && actions.Load != null);
                        Option(1, "불러오기", () => Open("load"), saves.Count > 0);
                        Option(2, "처음부터", actions.NewGame);
                        Option(3, "설정", () => Open("settings"));
                        Option(4, "끝내기", () => Open("quit"));
                        message.text = saves.Count == 0 ? "저장된 기록이 없습니다." : latest == null ? saves[0].Situation :
                            "저장 기록 " + saves.Count + " / " + SaveHistory.Capacity;
                    }
                    else
                    {
                        Option(0, "계속하기", actions.Resume);
                        Option(1, "저장하기", actions.Save);
                        Option(2, "불러오기", () => Open("load"));
                        Option(3, "가이드", () => Open("guide"));
                        Option(4, "설정", () => Open("settings"));
                        Option(5, "타이틀 화면", actions.Title);
                    }
                    break;
                case "load":
                    BuildSaveList(); break;
                case "guide":
                    MonitorWidgets.ScrollText(content, font, Vector2.zero, new Vector2(1000, 344)).text =
                        "화면 이동\n1 터미널 · 2 상담 · 3 프로필 · 4 서랍 · 5 축음기\nESC 물건 내려놓기 → 전체 보기 → 메뉴 · Q 전체 보기 · F5 빠른 저장\n입력 중에는 화면 이동과 빠른 저장 단축키가 작동하지 않습니다.\n\n" +
                        "터미널\n입력창을 클릭하거나 Enter로 명령창에 돌아올 수 있습니다.\n대상 뒤에 '.'을 입력하면 권한 목록을 볼 수 있습니다.\nTab / Enter로 자동완성 확정 · 후보 이름 입력 중 Space 확정\n다음 Enter로 명령 실행 · 입력하지 않을 때 Tab / Shift+Tab 메뉴 이동\nhelp / 도움말: 사용 가능한 명령 확인\n\n" +
                        "상담\n상담소에서 프로필과 상담 일지를 기록할 수 있습니다.\n스케쥴에서 다음 예약까지 기다리거나 상담을 마칠 수 있습니다.\n\n" +
                        "물건 확인\n물건을 클릭해서 확인하고 '내려놓기'로 배치하세요.\n배치 중 서랍 클릭 / 4 서랍 앵글 · 서랍 앞면 클릭: 열기 / 닫기\n우클릭: 배치 취소 · 종이의 인쇄 영역 클릭: 내용 읽기";
                    Option(5, "돌아가기", () => Open("main"));
                    break;
                case "settings": BuildSettings(); break;
                case "quit":
                    Option(1, "아니요", () => Open("main"));
                    Option(2, "예, 끝내기", actions.Quit);
                    footer.text = "ESC 취소";
                    break;
            }
            ConfigureNavigation();
        }
        private IReadOnlyList<SystemSaveInfo> ReadSaves()
        {
            if (!savesRead) { saveSlots = actions.ReadSaves?.Invoke() ?? Array.Empty<SystemSaveInfo>(); savesRead = true; }
            return saveSlots;
        }
        private void BuildSaveList()
        {
            saveSlots = ReadSaves();
            MonitorWidgets.Label(content, font, Vector2.zero, new Vector2(1000, 40), "저장 기록 " + saveSlots.Count + " / " + SaveHistory.Capacity + " · 최신순", 25);
            RectTransform viewport = MonitorWidgets.Rect("SaveList", content, new Vector2(0, -50), new Vector2(1000, 326));
            viewport.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.03f);
            viewport.gameObject.AddComponent<CrtMeshEffect>(); viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform rows = MonitorWidgets.Rect("SaveSlots", viewport, Vector2.zero, new Vector2(1000, Math.Max(326, saveSlots.Count * 120)));
            var scroll = viewport.gameObject.AddComponent<CrtScrollRect>(); scroll.content = rows; scroll.viewport = viewport;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 36;
            if (!saveSlots.Any(s => s.Id == selectedSaveId && s.Available)) selectedSaveId = saveSlots.FirstOrDefault(s => s.Available)?.Id;
            for (int i = 0; i < saveSlots.Count; i++)
            {
                SystemSaveInfo slot = saveSlots[i];
                RectTransform row = MonitorWidgets.Rect("Save_" + slot.Id, rows, new Vector2(0, -i * 120), new Vector2(1000, 112));
                Image background = row.gameObject.AddComponent<Image>(); background.color = MonitorWidgets.ButtonColor; row.gameObject.AddComponent<CrtMeshEffect>();
                SystemSaveRow button = row.gameObject.AddComponent<SystemSaveRow>(); button.targetGraphic = background;
                button.onClick.AddListener(() => { if (CanUse) SelectSave(slot.Id); });
                button.Activated = () => { if (CanUse) { SelectSave(slot.Id); LoadSelectedSave(); } };
                button.Focused = () => { if (CanUse) SelectSave(slot.Id); ScrollToRow(scroll, row); };
                SaveCell(row, "GameTime", Vector2.zero, new Vector2(177, 112), slot.Day > 0 ? "DAY " + slot.Day + "\n" + slot.GameTime : "기록 없음", 28, TextAnchor.MiddleCenter);
                SaveCell(row, "Situation", new Vector2(185, 0), new Vector2(815, 64), slot.Situation, 22, TextAnchor.MiddleLeft, 126);
                SaveCell(row, "SavedAt", new Vector2(185, -68), new Vector2(815, 44), slot.SavedAt, 20, TextAnchor.MiddleLeft);
                Text kind = MonitorWidgets.Label(row, font, new Vector2(872, -10), new Vector2(116, 44), slot.Kind, 19); kind.alignment = TextAnchor.MiddleRight;
                button.interactable = slot.Available;
                menuControls.Add(button);
                if (slot.Id != null) saveButtons[slot.Id] = button;
            }
            if (saveSlots.Count == 0) MonitorWidgets.Label(rows, font, new Vector2(20, -20), new Vector2(960, 160), "저장된 기록이 없습니다.", 26);
            loadButton = MonitorWidgets.Button(content, font, new Vector2(0, -398), new Vector2(650, 52), "이 기록 불러오기", () =>
            { if (CanUse) LoadSelectedSave(); });
            menuControls.Add(loadButton);
            menuControls.Add(MonitorWidgets.Button(content, font, new Vector2(674, -398), new Vector2(326, 52), "돌아가기", () => Run(() => Open("main"))));
            SelectSave(selectedSaveId);
            message.text = "새 저장은 맨 위에 추가됩니다. 10개를 넘으면 가장 오래된 기록부터 삭제됩니다.";
        }
        private void LoadSelectedSave()
        { if (saveSlots.Any(s => s.Id == selectedSaveId && s.Available)) actions.Load?.Invoke(selectedSaveId); }
        private void SaveCell(Transform row, string name, Vector2 position, Vector2 size, string value, int fontSize, TextAnchor alignment, float reservedRight = 0)
        {
            RectTransform cell = MonitorWidgets.Rect(name, row, position, size);
            Image background = cell.gameObject.AddComponent<Image>(); background.color = new Color(0, 0, 0, 0.16f); background.raycastTarget = false;
            cell.gameObject.AddComponent<CrtMeshEffect>();
            Text label = MonitorWidgets.Label(cell, font, new Vector2(14, -4), size - new Vector2(28 + reservedRight, 8), value ?? "", fontSize);
            label.alignment = alignment;
        }
        private static void ScrollToRow(ScrollRect scroll, RectTransform row)
        {
            float top = -row.anchoredPosition.y, offset = scroll.content.anchoredPosition.y, height = scroll.viewport.rect.height;
            if (top < offset) offset = top; else if (top + row.rect.height > offset + height) offset = top + row.rect.height - height;
            scroll.StopMovement(); scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Mathf.Max(0, scroll.content.rect.height - height)));
        }
        private void ConfigureNavigation()
        {
            Selectable[] enabled = menuControls.Where(b => b != null && b.isActiveAndEnabled && b.interactable).ToArray();
            for (int i = 0; i < enabled.Length; i++)
            {
                Selectable previous = enabled[(i + enabled.Length - 1) % enabled.Length], next = enabled[(i + 1) % enabled.Length];
                enabled[i].navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = previous, selectOnDown = next,
                    selectOnLeft = previous, selectOnRight = next };
            }
            if (section == "settings") ConfigureSettingsNavigation(enabled);
            if (EventSystem.current != null && enabled.Length > 0)
                DefaultFocus()?.Select();
        }
        private Selectable DefaultFocus()
        {
            if (section == "settings" && settingsTabs.Count > settingsCategory) return settingsTabs[settingsCategory];
            if (section == "load" && selectedSaveId != null && saveButtons.TryGetValue(selectedSaveId, out Button selected) && selected.interactable) return selected;
            return menuControls.FirstOrDefault(control => control != null && control.isActiveAndEnabled && control.interactable);
        }
        private void LateUpdate()
        {
            if (!CanUse || EventSystem.current == null) return;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected == null || !selected.activeInHierarchy || !selected.transform.IsChildOf(transform))
                DefaultFocus()?.Select();
        }
        public void SelectSave(string id)
        {
            if (id != null && !saveSlots.Any(s => s.Id == id && s.Available)) return;
            selectedSaveId = id;
            foreach (var pair in saveButtons) pair.Value.image.color = pair.Key == id ? new Color(0.16f, 0.32f, 0.23f) : MonitorWidgets.ButtonColor;
            if (loadButton != null) loadButton.interactable = id != null && actions.Load != null;
        }
        private void BuildSettings()
        {
            string[] categories = { "게임플레이", "비디오", "오디오", "입력" };
            for (int i = 0; i < categories.Length; i++)
            {
                int index = i;
                RectTransform rect = MonitorWidgets.Rect(categories[i], content, new Vector2(i * 254, 0), new Vector2(238, 48));
                Image image = rect.gameObject.AddComponent<Image>(); image.color = i == settingsCategory ? new Color(0.16f, 0.32f, 0.23f) : MonitorWidgets.ButtonColor;
                rect.gameObject.AddComponent<CrtMeshEffect>();
                SystemSettingChoice tab = rect.gameObject.AddComponent<SystemSettingChoice>(); tab.targetGraphic = image;
                Text text = MonitorWidgets.Label(rect, font, new Vector2(6, -4), new Vector2(226, 40), categories[i], 22); text.alignment = TextAnchor.MiddleCenter;
                tab.Adjust = direction => Run(() => ShowSettingsCategory(index + direction, false));
                tab.onClick.AddListener(() => Run(() => ShowSettingsCategory(index, true)));
                settingsTabs.Add(tab); menuControls.Add(tab);
            }
            RectTransform page = MonitorWidgets.Rect("SettingsPage", content, new Vector2(0, -78), new Vector2(1000, 296));
            switch (settingsCategory)
            {
                case 0:
                    AddSettingChoice(page, "언어", () => (actions.GetLanguage?.Invoke() ?? CommandLocale.Korean) == CommandLocale.Korean ? "한국어" : "English",
                        () => actions.SetLanguage?.Invoke((actions.GetLanguage?.Invoke() ?? CommandLocale.Korean) == CommandLocale.Korean ? CommandLocale.English : CommandLocale.Korean), actions.SetLanguage != null);
                    break;
                case 1:
                    bool fullscreen = Screen.fullScreen;
                    AddSettingChoice(page, "화면", () => fullscreen ? "전체 화면" : "창 모드", () =>
                    { fullscreen = !fullscreen; Screen.fullScreen = fullscreen; PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0); PlayerPrefs.Save(); });
                    break;
                case 2: BuildVolume(page); break;
                case 3:
                    string[,] bindings = { { "뒤로 / 메뉴", "ESC" }, { "전체 보기", "Q" }, { "화면 이동", "1 / 2 / 3 / 4 / 5" },
                        { "권한 확정", "Tab / Enter · 후보 이름 입력 중 Space" }, { "페이지 이동", "Tab / Shift+Tab (입력 중 제외)" }, { "빠른 저장", "F5" },
                        { "물건 확인 / 배치", "마우스 왼쪽 버튼 / 내려놓기" }, { "배치 취소", "마우스 오른쪽 버튼" } };
                    for (int i = 0; i < bindings.GetLength(0); i++)
                    {
                        MonitorWidgets.Label(page, font, new Vector2(20, -i * 34), new Vector2(330, 32), bindings[i, 0], 22);
                        MonitorWidgets.Label(page, font, new Vector2(370, -i * 34), new Vector2(610, 32), bindings[i, 1], 22);
                    }
                    break;
            }
            menuControls.Add(MonitorWidgets.Button(content, font, new Vector2(674, -398), new Vector2(326, 52), "돌아가기", () => Run(() => Open("main"))));
            footer.text = "↑↓ 항목 · ←→ 값 조절 · Enter 선택 · ESC 돌아가기";
            message.text = settingsCategory == 3 ? "ESC: 물건 내려놓기 → 전체 보기 → 메뉴" : "설정은 변경 즉시 적용되고 저장됩니다.";
        }
        private void ShowSettingsCategory(int index, bool focusBody)
        {
            settingsCategory = (index + 4) % 4; Render();
            if (focusBody) menuControls.FirstOrDefault(control => !settingsTabs.Contains(control as Button) && control.interactable)?.Select();
        }
        private void AddSettingChoice(Transform parent, string label, Func<string> value, Action change, bool enabled = true)
        {
            RectTransform row = MonitorWidgets.Rect(label, parent, Vector2.zero, new Vector2(1000, 76));
            Image background = row.gameObject.AddComponent<Image>(); background.color = MonitorWidgets.ButtonColor; row.gameObject.AddComponent<CrtMeshEffect>();
            var choice = row.gameObject.AddComponent<SystemSettingChoice>(); choice.targetGraphic = background; choice.interactable = enabled;
            MonitorWidgets.Label(row, font, new Vector2(18, -12), new Vector2(390, 48), label, 26);
            Text current = MonitorWidgets.Label(row, font, new Vector2(430, -12), new Vector2(550, 48), value(), 26); current.alignment = TextAnchor.MiddleRight;
            choice.Adjust = _ => Run(() => { change(); current.text = value(); });
            choice.onClick.AddListener(() => choice.Adjust?.Invoke(1)); menuControls.Add(choice);
        }
        private void BuildVolume(Transform parent)
        {
            RectTransform row = MonitorWidgets.Rect("MasterVolume", parent, Vector2.zero, new Vector2(1000, 76));
            Image background = row.gameObject.AddComponent<Image>(); background.color = MonitorWidgets.ButtonColor; row.gameObject.AddComponent<CrtMeshEffect>();
            var slider = row.gameObject.AddComponent<CrtSlider>(); slider.targetGraphic = background; slider.minValue = 0; slider.maxValue = 1;
            MonitorWidgets.Label(row, font, new Vector2(18, -12), new Vector2(300, 48), "음량", 26);
            RectTransform track = MonitorWidgets.Rect("VolumeTrack", row, new Vector2(350, -26), new Vector2(480, 24));
            Image trackImage = track.gameObject.AddComponent<Image>(); trackImage.color = new Color(0.02f, 0.04f, 0.03f); track.gameObject.AddComponent<CrtMeshEffect>();
            RectTransform fill = MonitorWidgets.Rect("VolumeFill", track, Vector2.zero, new Vector2(480, 24));
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
            Image gauge = fill.gameObject.AddComponent<Image>(); gauge.color = MonitorWidgets.Foreground;
            gauge.raycastTarget = false; fill.gameObject.AddComponent<CrtMeshEffect>();
            slider.fillRect = fill; slider.Track = track;
            Text percentage = MonitorWidgets.Label(row, font, new Vector2(860, -12), new Vector2(120, 48), "", 26); percentage.alignment = TextAnchor.MiddleRight;
            void RefreshVolume() => percentage.text = Mathf.RoundToInt(AudioListener.volume * 100) + "%";
            slider.SetValueWithoutNotify(AudioListener.volume); RefreshVolume();
            slider.onValueChanged.AddListener(value =>
            {
                if (!CanUse) return;
                AudioListener.volume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume); PlayerPrefs.Save(); RefreshVolume();
            });
            menuControls.Add(slider);
        }
        private void ConfigureSettingsNavigation(Selectable[] enabled)
        {
            Selectable[] rows = enabled.Where(control => !settingsTabs.Contains(control as Button)).ToArray();
            Button active = settingsTabs[settingsCategory];
            for (int i = 0; i < settingsTabs.Count; i++)
                settingsTabs[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnLeft = settingsTabs[(i + settingsTabs.Count - 1) % settingsTabs.Count], selectOnRight = settingsTabs[(i + 1) % settingsTabs.Count],
                    selectOnUp = rows.LastOrDefault(), selectOnDown = rows.FirstOrDefault() };
            for (int i = 0; i < rows.Length; i++)
                rows[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = i == 0 ? (Selectable)active : rows[i - 1], selectOnDown = i + 1 == rows.Length ? (Selectable)active : rows[i + 1] };
        }
    }
}
