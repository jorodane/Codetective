using System;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>Monitor controls share the CRT mesh and curved pointer projection with the console.</summary>
    internal static class MonitorWidgets
    {
        public static readonly Color Foreground = new Color(0.70f, 0.93f, 0.78f);
        public static readonly Color ButtonColor = new Color(0.075f, 0.16f, 0.12f);
        public static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); rect.gameObject.layer = 5;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }
        public static Text Label(Transform parent, Font font, Vector2 position, Vector2 size, string value, int fontSize = 24)
        {
            RectTransform rect = Rect("Label", parent, position, size);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = fontSize; text.text = value;
            text.color = Foreground; text.supportRichText = false; text.raycastTarget = false;
            rect.gameObject.AddComponent<CrtMeshEffect>();
            return text;
        }
        public static Button Button(Transform parent, Font font, Vector2 position, Vector2 size, string value, Action action)
        {
            RectTransform rect = Rect(value, parent, position, size);
            Image image = rect.gameObject.AddComponent<Image>(); image.color = ButtonColor;
            rect.gameObject.AddComponent<CrtMeshEffect>();
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            button.onClick.AddListener(() => action());
            Text label = Label(rect, font, new Vector2(6, -4), size - new Vector2(12, 8), value, 22);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        public static Text ScrollText(Transform parent, Font font, Vector2 position, Vector2 size)
        {
            RectTransform viewport = Rect("Records", parent, position, size);
            Image background = viewport.gameObject.AddComponent<Image>(); background.color = new Color(0.02f, 0.04f, 0.03f);
            viewport.gameObject.AddComponent<CrtMeshEffect>(); viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<CrtScrollRect>();
            Text text = Label(viewport, font, Vector2.zero, new Vector2(size.x - 14, 0), "", 26);
            text.rectTransform.anchorMax = new Vector2(1, 1); text.rectTransform.sizeDelta = new Vector2(-14, 0);
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = text.rectTransform; scroll.viewport = viewport; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            return text;
        }
    }
}
