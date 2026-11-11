using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Interaction
{
    [DisallowMultipleComponent]
    public sealed class PauseMenuView : MonoBehaviour
    {
        [SerializeField] private Font font;
        public event Action ResumeRequested;

        private GameObject root;
        private Button resumeButton;

        public void Show()
        {
            if (root == null)
                Build();
            root.SetActive(true);
            resumeButton.Select();
        }

        public void Hide()
        {
            if (root == null)
                return;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.transform.IsChildOf(root.transform))
                EventSystem.current.SetSelectedGameObject(null);
            root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (root != null)
                Destroy(root);
        }

        private void Build()
        {
            root = new GameObject("GameMenu", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform backdrop = Rect("Backdrop", root.transform, Vector2.zero, Vector2.zero);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
            RectTransform panel = Rect("Panel", backdrop, Vector2.zero, new Vector2(480, 300));
            panel.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.07f, 0.055f, 1f);
            Label("Title", panel, new Vector2(0, 86), new Vector2(420, 60), "일시 정지", 36);
            Label("Hint", panel, new Vector2(0, 20), new Vector2(420, 60), "ESC를 누르면 전체 보기로 돌아갑니다.", 20);

            RectTransform button = Rect("Resume", panel, new Vector2(0, -76), new Vector2(300, 56));
            var image = button.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.28f, 0.20f, 1f);
            resumeButton = button.gameObject.AddComponent<Button>();
            resumeButton.targetGraphic = image;
            resumeButton.onClick.AddListener(() => ResumeRequested?.Invoke());
            Label("Label", button, Vector2.zero, new Vector2(280, 50), "계속하기", 26);
        }

        private void Label(string name, Transform parent, Vector2 position, Vector2 size, string value, int fontSize)
        {
            Text text = Rect(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.70f, 0.96f, 0.80f, 1f);
            text.raycastTarget = false;
            text.supportRichText = false;
            text.text = value;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
