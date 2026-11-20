using System;
using System.Collections;
using System.Linq;
using Codetective.Operations;
using Codetective.Terminal;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    /// <summary>Prints on and animates the existing sheet. The sheet is never parented to the camera.</summary>
    public sealed class DeskProfilePresenter : MonoBehaviour
    {
        private KernelRuntime runtime;
        private Transform sheet, stack;
        private Func<string, Sprite> portraitFor;
        private Func<bool> canRead, paused;
        private Action<string> selected;
        private RectTransform ink;
        private CanvasGroup interaction;
        private ProfileFormView form;
        private Text journal, pageLabel, heading;
        private Button previous, next;
        private Vector3 restingPosition, sheetCenter;
        private Quaternion restingRotation;
        private int pageIndex, revision = -1;
        private Coroutine swapping;
        public string ProfileId { get; private set; }
        public bool IsSwapping { get; private set; }
        public Transform Sheet => sheet;
        private Vector3 DeskPosition => sheet.parent != null ? sheet.parent.TransformPoint(restingPosition) : restingPosition;
        private Quaternion DeskRotation => sheet.parent != null ? sheet.parent.rotation * restingRotation : restingRotation;

        public void Initialize(KernelRuntime world, Transform authoredSheet, Transform sourceStack, Camera camera, Font font,
            string initialProfile, Func<string, Sprite> portrait, Func<bool> readable, Func<bool> isPaused, Action<string> onSelected)
        {
            runtime = world; sheet = authoredSheet; stack = sourceStack; portraitFor = portrait; canRead = readable; paused = isPaused; selected = onSelected;
            if (sheet == null) return;
            restingPosition = sheet.localPosition; restingRotation = sheet.localRotation;
            Bounds bounds = LocalBounds(sheet); sheetCenter = bounds.center;
            Vector3 scale = sheet.lossyScale;
            float width = Mathf.Max(0.01f, bounds.size.x * Mathf.Abs(scale.x)) * 0.90f;
            float height = Mathf.Max(0.01f, bounds.size.z * Mathf.Abs(scale.z)) * 0.90f;
            float logicalHeight = 900 * height / width;
            ink = (RectTransform)new GameObject("ProfileInk", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup)).transform;
            ink.SetParent(sheet, false); ink.sizeDelta = new Vector2(900, logicalHeight);
            Vector3 center = sheet.TransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)) + Vector3.up * 0.0006f;
            ink.SetPositionAndRotation(center, Quaternion.Euler(90, 0, 0)); ink.localScale = Vector3.one;
            float units = width / 900;
            ink.localScale = new Vector3(units / ink.TransformVector(Vector3.right).magnitude, units / ink.TransformVector(Vector3.up).magnitude, units);
            Canvas canvas = ink.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            interaction = ink.GetComponent<CanvasGroup>(); interaction.interactable = interaction.blocksRaycasts = false;
            ink.gameObject.AddComponent<Image>().color = new Color(0.87f, 0.84f, 0.75f);
            Font typeface = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            heading = InkLabel(typeface, new Vector2(24, -18), new Vector2(852, 40), "인물 프로필", 30);
            form = ProfileFormView.CreateHeader(ink, typeface, 852, false, true); form.transform.localPosition += new Vector3(24, -78, 0);
            InkLabel(typeface, new Vector2(24, -338), new Vector2(852, 42), "상담 일지", 32);
            RectTransform viewport = OverlayWidgets.Rect("ConsultationJournal", ink, new Vector2(24, -386), new Vector2(852, Mathf.Max(80, logicalHeight - 482)));
            viewport.gameObject.AddComponent<Image>().color = new Color(0.87f, 0.84f, 0.75f); viewport.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false; scroll.scrollSensitivity = 35; scroll.movementType = ScrollRect.MovementType.Clamped;
            journal = OverlayWidgets.Label(viewport, typeface, Vector2.zero, new Vector2(836, 1), "", 30); journal.color = new Color(0.12f, 0.11f, 0.08f);
            journal.verticalOverflow = VerticalWrapMode.Overflow; journal.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = journal.rectTransform;
            previous = InkButton(typeface, new Vector2(24, -logicalHeight + 70), "‹", () => TurnPage(-1));
            next = InkButton(typeface, new Vector2(786, -logicalHeight + 70), "›", () => TurnPage(1));
            pageLabel = InkLabel(typeface, new Vector2(240, -logicalHeight + 66), new Vector2(420, 42), "", 30); pageLabel.alignment = TextAnchor.MiddleCenter;
            Commit(runtime.VisibleProfileIds.Contains(initialProfile) ? initialProfile : runtime.VisibleProfileIds.FirstOrDefault());
        }
        public bool SelectProfile(string id)
        {
            if (sheet == null || IsSwapping || !(canRead?.Invoke() ?? false) || !runtime.VisibleProfileIds.Contains(id)) return false;
            if (ProfileId == id) return true;
            if (!Application.isPlaying || !isActiveAndEnabled) { Commit(id); return true; }
            swapping = StartCoroutine(Swap(id)); return true;
        }
        private IEnumerator Swap(string id)
        {
            IsSwapping = true;
            try
            {
                Vector3 stackPosition = stack != null ? StackTop(stack) - sheet.TransformVector(sheetCenter) : DeskPosition + Vector3.left * 0.25f;
                yield return Move(sheet.position, stackPosition, 0.25f);
                if (runtime.VisibleProfileIds.Contains(id)) Commit(id);
                yield return Move(stackPosition, DeskPosition, 0.3f);
            }
            finally { RestorePose(); IsSwapping = false; swapping = null; }
        }
        private IEnumerator Move(Vector3 from, Vector3 to, float seconds)
        {
            for (float time = 0; time < seconds;)
            {
                if (paused?.Invoke() ?? false) { yield return null; continue; }
                float t = Mathf.Clamp01(time / seconds); float blend = Mathf.SmoothStep(0, 1, t);
                sheet.SetPositionAndRotation(Vector3.Lerp(from, to, blend) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.035f), DeskRotation);
                time += Time.unscaledDeltaTime; yield return null;
            }
            sheet.SetPositionAndRotation(to, DeskRotation);
        }
        public void CancelSwap()
        {
            if (swapping != null) StopCoroutine(swapping);
            swapping = null; IsSwapping = false; RestorePose();
        }
        private void RestorePose() { if (sheet != null) { sheet.localPosition = restingPosition; sheet.localRotation = restingRotation; } }
        private void Commit(string id) { ProfileId = id; pageIndex = 0; revision = -1; Refresh(); selected?.Invoke(id); }
        private void TurnPage(int direction)
        {
            if (IsSwapping || !(canRead?.Invoke() ?? false) || ProfileId == null) return;
            pageIndex = Mathf.Clamp(pageIndex + direction, 0, Mathf.Max(0, runtime.NotesFor(ProfileId).Count() - 1)); revision = -1; Refresh();
        }
        private void Update()
        {
            if (sheet == null || ink == null) return;
            bool readable = !IsSwapping && (canRead?.Invoke() ?? false);
            interaction.interactable = interaction.blocksRaycasts = readable;
            if (revision != runtime.Revision) Refresh();
        }
        public void Refresh()
        {
            if (runtime == null || form == null) return;
            if (ProfileId == null || !runtime.VisibleProfileIds.Contains(ProfileId))
            {
                ProfileId = null; form.gameObject.SetActive(false); heading.text = "인물 프로필";
                journal.text = "오른쪽 인물 목록에서 열람할 대상을 선택하세요."; previous.interactable = next.interactable = false; pageLabel.text = "";
            }
            else
            {
                PersonProfile card = runtime.ProfileCard(ProfileId); form.gameObject.SetActive(true); form.SetProfile(card, portraitFor?.Invoke(ProfileId));
                heading.text = card.DisplayName + " / 인물 프로필";
                int count = Math.Max(1, runtime.NotesFor(ProfileId).Count()); pageIndex = Mathf.Clamp(pageIndex, 0, count - 1);
                journal.text = runtime.ProfileJournalText(ProfileId, pageIndex); pageLabel.text = (pageIndex + 1) + " / " + count;
                previous.interactable = pageIndex > 0; next.interactable = pageIndex + 1 < count;
            }
            revision = runtime.Revision;
        }
        private Text InkLabel(Font font, Vector2 position, Vector2 size, string text, int fontSize)
        { Text label = OverlayWidgets.Label(ink, font, position, size, text, fontSize); label.color = new Color(0.12f, 0.11f, 0.08f); return label; }
        private Button InkButton(Font font, Vector2 position, string text, Action action)
        {
            Button button = OverlayWidgets.Button(ink, font, position, new Vector2(90, 48), text, action); button.image.color = Color.clear;
            Text label = button.GetComponentInChildren<Text>(); label.color = new Color(0.12f, 0.11f, 0.08f); label.fontSize = 32;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; return button;
        }
        private static Bounds LocalBounds(Transform root)
        {
            bool found = false; Bounds result = default;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = root.InverseTransformPoint(renderer.transform.TransformPoint(renderer.localBounds.center + Vector3.Scale(renderer.localBounds.extents, new Vector3(x, y, z))));
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; } else result.Encapsulate(point);
                }
            return found ? result : new Bounds(Vector3.zero, new Vector3(0.21f, 0.002f, 0.297f));
        }
        private static Vector3 StackTop(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return root.position;
            Bounds bounds = renderers[0].bounds; foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return new Vector3(bounds.center.x, bounds.max.y + 0.003f, bounds.center.z);
        }
        private void OnDisable() => CancelSwap();
        private void OnDestroy() { CancelSwap(); if (ink != null) ProfileModelUtility.Dispose(ink.gameObject); }
    }
}
