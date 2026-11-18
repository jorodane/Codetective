using System;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed class SystemSaveInfo
    {
        public bool Available;
        public string Description;
    }
    public sealed class SystemMenuActions
    {
        public Action Resume, Continue, NewGame, Load, Title, Quit;
        public Func<SystemSaveInfo> ReadSave;
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
            IsTitle = title; section = "main";
            host.SetSystemMenuVisible(true); transform.SetAsLastSibling(); gameObject.SetActive(true); Render();
        }
        public void Hide() { gameObject.SetActive(false); if (host != null) host.SetSystemMenuVisible(false); }
        // Escape goes back within system screens; only the in-game root may resume gameplay.
        public bool HandleEscape()
        {
            if (section != "main") { Open("main"); return true; }
            return IsTitle;
        }
        public void ShowFeedback(string value) { if (message != null) message.text = value; }
        private void Open(string value) { section = value; Render(); }
        private bool CanUse => isActiveAndEnabled && host.SystemMenuVisible && host.GetComponent<TerminalController>().InteractionEnabled;
        private void Run(Action action) { if (CanUse) action?.Invoke(); }
        private Button Option(int row, string label, Action action, bool enabled = true)
        {
            Button button = MonitorWidgets.Button(content, font, new Vector2(268, -row * 76), new Vector2(464, 58), label, () => { if (enabled) Run(action); });
            button.interactable = enabled; return button;
        }
        private void Render()
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject; child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            heading.text = section == "main" ? IsTitle ? "CODETECTIVE" : "일시정지됨" : section == "load" ? "불러오기" : section == "guide" ? "가이드" : "설정";
            message.text = "";
            footer.text = section != "main" ? "ESC 돌아가기" : IsTitle ? "CODETECTIVE / KERNEL TERMINAL" : "ESC 계속하기";
            switch (section)
            {
                case "main":
                    if (IsTitle)
                    {
                        SystemSaveInfo save = ReadSave();
                        Option(0, "이어하기", actions.Continue, save.Available);
                        Option(1, "처음부터", actions.NewGame);
                        Option(2, "설정", () => Open("settings"));
                        Option(3, "끝내기", actions.Quit);
                        message.text = save.Description;
                    }
                    else
                    {
                        Option(0, "계속하기", actions.Resume);
                        Option(1, "불러오기", () => Open("load"));
                        Option(2, "가이드", () => Open("guide"));
                        Option(3, "설정", () => Open("settings"));
                        Option(4, "타이틀 화면", actions.Title);
                    }
                    break;
                case "load":
                    SystemSaveInfo saved = ReadSave();
                    MonitorWidgets.Label(content, font, Vector2.zero, new Vector2(1000, 170), "날짜 자동 저장\n\n" + saved.Description, 28);
                    Option(3, "이 기록 불러오기", actions.Load, saved.Available);
                    Option(4, "돌아가기", () => Open("main"));
                    break;
                case "guide":
                    MonitorWidgets.ScrollText(content, font, Vector2.zero, new Vector2(1000, 344)).text =
                        "화면 이동\n1 터미널 · 2 상담 · 3 프로필 · 4 서랍 · 5 축음기\nQ 전체 보기 · ESC 메뉴\n입력 중에는 화면 이동 단축키가 작동하지 않습니다.\n\n" +
                        "터미널\n입력창을 클릭해서 명령을 작성하세요.\n대상 뒤에 '.'을 입력하면 권한 목록을 볼 수 있습니다.\nTab / Enter / Space 자동완성 확정 · 다음 Enter 명령 실행\n입력하지 않을 때 Tab / Shift+Tab 메뉴 이동\nhelp / 도움말: 사용 가능한 명령 확인\n\n" +
                        "확보 자료와 상담\n커널 → 확보 자료에서 도구와 단서를 보고 명령용 참조를 연결하세요.\n상담소에서 프로필과 상담 일지를 기록할 수 있습니다.\n스케쥴에서 다음 예약까지 기다리거나 상담을 마칠 수 있습니다.\n\n" +
                        "물건 확인\n물건을 클릭해서 확인하고 '내려놓기'로 배치하세요.\n4 서랍 앵글 · 서랍 앞면 클릭: 열기 / 닫기\n우클릭: 배치 취소 · 종이의 인쇄 영역 클릭: 내용 읽기";
                    Option(5, "돌아가기", () => Open("main"));
                    break;
                case "settings": BuildSettings(); break;
            }
        }
        private SystemSaveInfo ReadSave() => actions.ReadSave?.Invoke() ?? new SystemSaveInfo { Description = "저장된 기록이 없습니다." };
        private void BuildSettings()
        {
            Text volume = MonitorWidgets.Label(content, font, new Vector2(268, -8), new Vector2(464, 48), "", 28);
            void RefreshVolume() => volume.text = "전체 음량  " + Mathf.RoundToInt(AudioListener.volume * 100) + "%";
            void ChangeVolume(float delta)
            {
                AudioListener.volume = Mathf.Clamp01(AudioListener.volume + delta);
                PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume); PlayerPrefs.Save(); RefreshVolume();
            }
            MonitorWidgets.Button(content, font, new Vector2(160, 0), new Vector2(76, 58), "−", () => Run(() => ChangeVolume(-0.1f)));
            MonitorWidgets.Button(content, font, new Vector2(764, 0), new Vector2(76, 58), "+", () => Run(() => ChangeVolume(0.1f)));
            RefreshVolume();
            Button fullscreen = null;
            fullscreen = Option(1, "화면: " + (Screen.fullScreen ? "전체 화면" : "창 모드"), () =>
            {
                bool next = !Screen.fullScreen;
                Screen.fullScreen = next; PlayerPrefs.SetInt(FullscreenKey, next ? 1 : 0); PlayerPrefs.Save();
                fullscreen.GetComponentInChildren<Text>().text = "화면: " + (next ? "전체 화면" : "창 모드");
            });
            string LanguageLabel() => "명령 언어: " + ((actions.GetLanguage?.Invoke() ?? CommandLocale.Korean) == CommandLocale.Korean ? "한국어" : "English");
            Button language = null;
            language = Option(2, LanguageLabel(), () =>
            {
                actions.SetLanguage?.Invoke((actions.GetLanguage?.Invoke() ?? CommandLocale.Korean) == CommandLocale.Korean ? CommandLocale.English : CommandLocale.Korean);
                language.GetComponentInChildren<Text>().text = LanguageLabel();
            }, actions.SetLanguage != null);
            Option(4, "돌아가기", () => Open("main"));
            message.text = "설정은 변경 즉시 적용되고 저장됩니다.";
        }
    }
}
