using Codetective.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    /// <summary>The left side handles picking a printed area or dragging the held object.</summary>
    internal sealed class ItemInspectionView : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IDragHandler
    {
        private ItemCarryController carry;
        private RectTransform panel;
        private CanvasGroup panelVisibility;
        private Text title, description, hint;
        private Button use, reset, place, close;
        private HoldableItem displayed;
        private bool dragged;

        public static ItemInspectionView Create(Transform parent, Font font, KernelGameController game)
        {
            RectTransform pointer = OverlayWidgets.Rect("InspectItemPointer", parent, Vector2.zero, Vector2.zero);
            SetBounds(pointer, Vector2.zero, new Vector2(HoldableItem.InspectionLaneWidth, 1), Vector2.zero, Vector2.zero);
            Image surface = pointer.gameObject.AddComponent<Image>();
            surface.color = Color.clear;
            surface.canvasRenderer.cullTransparentMesh = false;
            var view = pointer.gameObject.AddComponent<ItemInspectionView>();
            view.carry = game.Carry;
            view.BuildPanel(parent, font, game);
            view.Show(null);
            return view;
        }

        private void BuildPanel(Transform parent, Font font, KernelGameController game)
        {
            panel = OverlayWidgets.Rect("HeldObjectDetails", parent, Vector2.zero, Vector2.zero);
            SetBounds(panel, new Vector2(0.65f, 0.15f), new Vector2(0.967f, 0.85f), Vector2.zero, Vector2.zero);
            panel.gameObject.AddComponent<Image>().color = new Color(0.045f, 0.073f, 0.061f, 0.97f);
            panelVisibility = panel.gameObject.AddComponent<CanvasGroup>();
            RectTransform line = OverlayWidgets.Rect("HeaderRule", panel, Vector2.zero, Vector2.zero);
            SetBounds(line, new Vector2(0, 1), Vector2.one, new Vector2(0, -2), Vector2.zero);
            Image accent = line.gameObject.AddComponent<Image>(); accent.color = new Color(0.57f, 0.60f, 0.43f); accent.raycastTarget = false;

            title = Label("ItemName", font, 32, new Vector2(0, 0.78f), new Vector2(1, 1), new Vector2(24, 8), new Vector2(-24, -22));
            title.color = new Color(0.95f, 0.92f, 0.80f);
            description = Label("ItemDescription", font, 20, new Vector2(0, 0.40f), new Vector2(1, 0.78f), new Vector2(24, 0), new Vector2(-24, 0));
            hint = Label("InspectionHint", font, 16, new Vector2(0, 0.25f), new Vector2(1, 0.44f), new Vector2(24, 0), new Vector2(-24, 0));
            hint.color = new Color(0.60f, 0.73f, 0.64f);

            use = Action(font, "사용하기", 0, 0.5f, 68, () => carry.TryUse(false));
            reset = Action(font, "정면으로", 0.5f, 1, 68, () => carry.ResetInspectionRotation());
            close = Action(font, "닫기", 0, 0.5f, 20, game.CloseItemInspection);
            place = Action(font, "배치하기", 0.5f, 1, 20, game.BeginItemPlacement);
            place.image.color = new Color(0.20f, 0.31f, 0.24f);
        }

        private Text Label(string name, Font font, int size, Vector2 min, Vector2 max, Vector2 lower, Vector2 upper)
        {
            Text label = OverlayWidgets.Label(panel, font, Vector2.zero, Vector2.zero, "", size);
            label.name = name;
            SetBounds(label.rectTransform, min, max, lower, upper);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = size - 5;
            label.resizeTextMaxSize = size;
            return label;
        }

        private Button Action(Font font, string label, float left, float right, float bottom, System.Action action)
        {
            Button button = OverlayWidgets.Button(panel, font, Vector2.zero, Vector2.one, label, action);
            SetBounds((RectTransform)button.transform, new Vector2(left, 0), new Vector2(right, 0),
                new Vector2(left == 0 ? 24 : 5, bottom), new Vector2(right == 1 ? -24 : -5, bottom + 40));
            SetBounds(button.GetComponentInChildren<Text>().rectTransform, Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3));
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        private static void SetBounds(RectTransform rect, Vector2 min, Vector2 max, Vector2 lower, Vector2 upper)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = lower; rect.offsetMax = upper; }

        public void Show(HoldableItem item)
        {
            bool visible = item != null;
            panel.gameObject.SetActive(visible);
            gameObject.SetActive(visible);
            if (!visible) { displayed = null; return; }
            if (displayed != item)
            {
                displayed = item;
                panelVisibility.alpha = 0;
                panel.SetAsLastSibling();
                title.text = item.DisplayName;
                description.text = item.Description;
                hint.text = item.Use == HeldItemUse.Newspaper ? "기사를 클릭해 전문 보기\n드래그로 신문 회전" : "드래그로 물건 회전";
                use.gameObject.SetActive(item.HasUseAction);
            }
            panelVisibility.alpha = Application.isPlaying ? Mathf.MoveTowards(panelVisibility.alpha, 1, Time.unscaledDeltaTime / 0.22f) : 1;
            use.interactable = reset.interactable = place.interactable = close.interactable = carry.CanManipulate;
        }

        private Button PrintedButton(PointerEventData pointer) => carry.CanManipulate && carry.IsExamining &&
            displayed != null && displayed == carry.HeldItem && displayed.TryGetComponent(out PaperDocument paper) ? paper.PrintedButtonAt(pointer) : null;

        public void OnPointerDown(PointerEventData pointer)
        { if (pointer.button == PointerEventData.InputButton.Left) dragged = false; }

        public void OnPointerClick(PointerEventData pointer)
        {
            if (pointer.button == PointerEventData.InputButton.Left && !pointer.dragging && !dragged)
                PrintedButton(pointer)?.onClick.Invoke();
        }

        public void OnDrag(PointerEventData pointer)
        {
            if (pointer.button != PointerEventData.InputButton.Left || displayed != carry.HeldItem) return;
            dragged = true;
            carry.RotateInspection(pointer.delta * (235f / Mathf.Max(1, Screen.height)));
        }
    }
}
