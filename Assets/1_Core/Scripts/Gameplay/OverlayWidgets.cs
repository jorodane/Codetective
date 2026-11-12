using System;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    internal static class OverlayWidgets
    {
        public static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size, Vector2? anchor = null)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); rect.gameObject.layer = 5;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor ?? new Vector2(0, 1);
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        public static Text Label(Transform parent, Font font, Vector2 position, Vector2 size, string text, int fontSize)
        {
            Text label = Rect("Label", parent, position, size).gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = fontSize; label.text = text; label.supportRichText = false;
            label.color = new Color(0.8f, 0.95f, 0.84f); label.raycastTarget = false; return label;
        }
        public static Button Button(Transform parent, Font font, Vector2 position, Vector2 size, string text, Action action)
        {
            RectTransform rect = Rect(text, parent, position, size);
            Image image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.12f, 0.22f, 0.16f);
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(() => action());
            Text label = Label(rect, font, new Vector2(6, -3), size - new Vector2(12, 6), text, 20); label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
    }
}
