using System;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [Serializable] public sealed class ProfilePortrait
    {
        public string profileId;
        public Sprite photo;
        public StoryCondition availability = new StoryCondition();
    }

    /// <summary>The same 35 x 45 portrait and six-field form is used on the monitor and physical paper.</summary>
    public sealed class ProfileFormView : MonoBehaviour
    {
        private readonly Text[] fields = new Text[6];
        private Image photo;
        private Text missingPhoto;
        private AspectRatioFitter photoFit;
        public static ProfileFormView CreateHeader(Transform parent, Font font, float width, bool crt, bool largePrint = false)
        {
            RectTransform root = MonitorWidgets.Rect("ProfileForm", parent, Vector2.zero, new Vector2(900, largePrint ? 264 : 190));
            root.localScale = Vector3.one * (width / 900f);
            ProfileFormView view = root.gameObject.AddComponent<ProfileFormView>();
            Vector2 photoSize = largePrint ? new Vector2(180, 180 * 45f / 35f) : new Vector2(140, 180);
            RectTransform frame = MonitorWidgets.Rect("Portrait35x45", root, Vector2.zero, photoSize);
            Image background = frame.gameObject.AddComponent<Image>(); background.raycastTarget = false;
            background.color = crt ? new Color(0.04f, 0.09f, 0.06f) : new Color(0.72f, 0.70f, 0.63f);
            if (crt) background.gameObject.AddComponent<CrtMeshEffect>();
            frame.gameObject.AddComponent<RectMask2D>();
            view.photo = MonitorWidgets.Rect("PortraitPhoto", frame, Vector2.zero, photoSize).gameObject.AddComponent<Image>();
            view.photo.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            view.photo.raycastTarget = false; view.photoFit = view.photo.gameObject.AddComponent<AspectRatioFitter>();
            view.photoFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            if (crt) view.photo.gameObject.AddComponent<CrtMeshEffect>();
            view.missingPhoto = Label(frame, font, new Vector2(6, -6), photoSize - new Vector2(12, 12), "사진 미등록", largePrint ? 28 : 20, crt);
            view.missingPhoto.alignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < view.fields.Length; i++)
            {
                float left = largePrint ? 216 : 180, rowHeight = largePrint ? 44 : 30;
                view.fields[i] = Label(root, font, new Vector2(left, -i * rowHeight), new Vector2(900 - left, rowHeight - 1), "", largePrint ? 32 : 22, crt);
                view.fields[i].resizeTextForBestFit = true; view.fields[i].resizeTextMinSize = largePrint ? 18 : 12; view.fields[i].resizeTextMaxSize = largePrint ? 32 : 22;
            }
            return view;
        }
        public void SetProfile(PersonProfile profile, Sprite portrait)
        {
            string[] values = { profile.Name, profile.Gender, profile.Age, profile.Birthday, profile.Occupation, profile.Family };
            string[] names = { "이름", "성별", "나이", "생일", "직업", "가족관계" };
            for (int i = 0; i < fields.Length; i++) fields[i].text = names[i] + ": " + values[i];
            photo.sprite = portrait; photo.enabled = photoFit.enabled = portrait != null;
            if (portrait != null) photoFit.aspectRatio = portrait.rect.width / Mathf.Max(1, portrait.rect.height);
            missingPhoto.gameObject.SetActive(portrait == null);
            missingPhoto.text = profile.Id == "kernel" ? "[데이터 말소됨]" : "사진 미등록";
        }
        public static RectTransform CreatePage(Transform parent, Font font, PersonProfile profile, Sprite portrait, string title, string journal, float width, bool crt = false)
        {
            RectTransform page = MonitorWidgets.Rect("ProfileJournalPage", parent, Vector2.zero, new Vector2(900, 550));
            CreateHeader(page, font, 900, crt).SetProfile(profile, portrait);
            Label(page, font, new Vector2(0, -208), new Vector2(900, 40), title, 28, crt);
            Text body = Label(page, font, new Vector2(0, -264), new Vector2(900, 1), journal, 24, crt);
            body.verticalOverflow = VerticalWrapMode.Overflow;
            body.rectTransform.sizeDelta = new Vector2(900, body.preferredHeight + 12);
            page.sizeDelta = new Vector2(900, Mathf.Max(550, 264 + body.rectTransform.sizeDelta.y));
            RectTransform scaled = MonitorWidgets.Rect("ProfilePageLayout", parent, Vector2.zero, new Vector2(width, page.sizeDelta.y * width / 900));
            page.SetParent(scaled, false); page.localScale = Vector3.one * (width / 900);
            return scaled;
        }
        private static Text Label(Transform parent, Font font, Vector2 position, Vector2 size, string value, int fontSize, bool crt)
        {
            Text label = MonitorWidgets.Rect("ProfileField", parent, position, size).gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = fontSize; label.text = value; label.supportRichText = false; label.raycastTarget = false;
            label.color = crt ? MonitorWidgets.Foreground : new Color(0.12f, 0.11f, 0.08f);
            if (crt) label.gameObject.AddComponent<CrtMeshEffect>();
            return label;
        }
    }
}
