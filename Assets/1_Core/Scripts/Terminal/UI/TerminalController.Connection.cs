using System;
using System.Collections.Generic;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed partial class TerminalController
    {
        private const float ColumnGap = 24f;
        private static readonly Color ConnectionRuleColor = new Color(0.22f, 0.35f, 0.27f);
        private static readonly Color RequestColor = new Color(0.91f, 0.79f, 0.49f);
        private readonly List<(RectTransform rect, float left)> consoleLeftEdges = new List<(RectTransform, float)>();
        private readonly List<(RectTransform rect, float right)> candidateRightEdges = new List<(RectTransform, float)>();
        private RectTransform contactPanel;
        private Text contactStatus, contactName, unknownPortrait, contactActionLabel;
        private Image contactPortrait;
        private Button contactAction;
        private MonitorPageHost contactHost;
        private float contactWidth;
        private bool consoleSplit;
        private string shownConnectionId, shownRequestId, shownReconnectId, connectionLocale;
        private int contactRevision = -1, portraitRevision = -1;
        private bool CommandInputBlocked => Runtime.TerminalInputLocked || Runtime.TerminalIncomingConnectionId != null;

        private void RefreshConnectionPresentation()
        {
            string connected = Runtime.TerminalConversationActive ? Runtime.TerminalConversationId : null;
            string incoming = Runtime.TerminalIncomingConnectionId;
            string reconnect = incoming == null && connected == null ? Runtime.TerminalReconnectId : null;
            string contactId = incoming ?? connected ?? reconnect;
            bool split = contactId != null;
            if (contactPanel == null && (!pageVisible || !split || !BuildConnectionView())) return;

            if (consoleSplit != split) LayoutConnectedConsole(split);
            contactPanel.gameObject.SetActive(pageVisible && split);
            contactAction.interactable = interactionEnabled;

            int nextPortraitRevision = contactHost != null ? contactHost.ProfilePageRevision : 0;
            string locale = Runtime.DisplayLanguage;
            if (shownConnectionId == connected && shownRequestId == incoming && shownReconnectId == reconnect && connectionLocale == locale &&
                contactRevision == Runtime.Revision && portraitRevision == nextPortraitRevision) return;
            shownConnectionId = connected; shownRequestId = incoming; shownReconnectId = reconnect; connectionLocale = locale;
            contactRevision = Runtime.Revision; portraitRevision = nextPortraitRevision;

            if (!split) return;
            bool english = locale == "en";
            TerminalContact contact = Runtime.ContactForTerminalConversation(contactId);
            contactName.text = contact.Name;
            contactStatus.color = incoming != null ? RequestColor : MonitorWidgets.Foreground;
            contactAction.image.color = incoming != null ? MonitorWidgets.SelectedButtonColor : MonitorWidgets.ButtonColor;
            if (incoming != null)
            {
                contactStatus.text = english ? "Incoming connection" : "연결 요청";
                contactActionLabel.text = english ? "Accept" : "수락";
            }
            else if (connected != null)
            {
                contactStatus.text = english ? "Connected" : "연결됨";
                contactActionLabel.text = english ? "Disconnect" : "연결 끊기";
            }
            else
            {
                contactStatus.text = english ? "Disconnected" : "연결 끊김";
                contactActionLabel.text = english ? "Reconnect" : "다시 연결";
            }
            Sprite portrait = contactHost != null && !string.IsNullOrEmpty(contact.ProfileId) ? contactHost.PortraitFor(contact.ProfileId) : null;
            contactPortrait.sprite = portrait;
            contactPortrait.enabled = portrait != null;
            unknownPortrait.gameObject.SetActive(portrait == null);
        }

        private bool BuildConnectionView()
        {
            RectTransform field = (RectTransform)input.transform;
            if (!(field.parent is RectTransform page) || transcriptScroll.transform.parent != page || completionPanel.transform.parent != page) return false;
            float width = field.rect.width;
            if (width <= 0 || page.rect.height <= 0) return false;

            contactHost = GetComponent<MonitorPageHost>();
            contactWidth = Mathf.Round(width * 0.25f);
            RectTransform log = (RectTransform)transcriptScroll.transform;
            foreach (RectTransform rect in new[] { field, log, (RectTransform)completionPanel.transform })
                consoleLeftEdges.Add((rect, rect.offsetMin.x));
            foreach (RectTransform rect in completionPanel.transform) candidateRightEdges.Add((rect, rect.offsetMax.x));
            foreach (Text label in completionLabels)
                if (label != null && label.rectTransform.parent != completionPanel.transform && label.rectTransform.anchorMax.x == label.rectTransform.anchorMin.x)
                    candidateRightEdges.Add((label.rectTransform, label.rectTransform.offsetMax.x));

            Font font = input.textComponent.font;
            float left = field.anchoredPosition.x, top = log.anchoredPosition.y;
            float height = top - field.anchoredPosition.y + field.rect.height;
            const float padding = 14f;
            const float portraitTop = 66f;
            float innerWidth = contactWidth - padding * 2;
            Vector2 portraitSize = new Vector2(innerWidth, innerWidth * 3f / 4f);

            contactPanel = MonitorWidgets.Rect("TerminalContact", page, new Vector2(left, top), new Vector2(contactWidth, height));
            ContactGraphic("ContactDivider", contactPanel, new Vector2(contactWidth + ColumnGap * 0.5f, 0), new Vector2(2, height), ConnectionRuleColor);
            contactStatus = MonitorWidgets.Label(contactPanel, font, new Vector2(padding, -8), new Vector2(innerWidth, 32), "", 22);
            contactStatus.name = "ContactStatus";
            contactStatus.resizeTextForBestFit = true;
            contactStatus.resizeTextMinSize = 16;
            contactStatus.resizeTextMaxSize = contactStatus.fontSize;
            ContactGraphic("ContactRule", contactPanel, new Vector2(padding, -48), new Vector2(innerWidth, 2), ConnectionRuleColor);

            var frame = ContactGraphic("PortraitFrame", contactPanel, new Vector2(padding, -portraitTop), portraitSize, ConnectionRuleColor);
            Vector2 innerSize = portraitSize - new Vector2(4, 4);
            var portraitBacking = ContactGraphic("PortraitBacking", frame.transform, new Vector2(2, -2), innerSize, new Color(0.04f, 0.075f, 0.058f));
            contactPortrait = ContactGraphic("ContactPortrait", portraitBacking.transform, Vector2.zero, innerSize, Color.white);
            contactPortrait.preserveAspect = true;
            unknownPortrait = MonitorWidgets.Label(portraitBacking.transform, font, Vector2.zero, innerSize, "?", 64);
            unknownPortrait.name = "UnknownPortrait";
            unknownPortrait.color = ConnectionRuleColor;
            unknownPortrait.alignment = TextAnchor.MiddleCenter;

            contactName = MonitorWidgets.Label(contactPanel, font, new Vector2(padding, -portraitTop - portraitSize.y - 16), new Vector2(innerWidth, 80), "", 32);
            contactName.name = "ContactName";
            contactName.alignment = TextAnchor.UpperCenter;
            contactName.resizeTextForBestFit = true;
            contactName.resizeTextMinSize = 20;
            contactName.resizeTextMaxSize = contactName.fontSize;
            contactAction = MonitorWidgets.Button(contactPanel, font, new Vector2(padding, -height + 56), new Vector2(innerWidth, 44), "", UseContactAction);
            contactAction.name = "ContactAction";
            contactActionLabel = contactAction.GetComponentInChildren<Text>();
            return true;
        }

        private static Image ContactGraphic(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = MonitorWidgets.Rect(name, parent, position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            rect.gameObject.AddComponent<CrtMeshEffect>();
            return image;
        }

        private void LayoutConnectedConsole(bool split)
        {
            consoleSplit = split;
            float inset = split ? contactWidth + ColumnGap : 0;
            foreach (var (rect, left) in consoleLeftEdges)
                rect.offsetMin = new Vector2(left + inset, rect.offsetMin.y);
            foreach (var (rect, right) in candidateRightEdges)
                rect.offsetMax = new Vector2(right - inset, rect.offsetMax.y);
            if (answerRow != null)
            {
                float width = ((RectTransform)input.transform).rect.width;
                answerRow.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                answerChoices.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width - 114);
                questionPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                visibleAnswerKeys = Array.Empty<string>();
                answerQuestion.text = "";
            }
            QueueTranscriptScroll(toLatest: true);
        }

        private void UseContactAction()
        {
            if (!pageVisible || !interactionEnabled) return;
            bool changed;
            if (shownRequestId != null) changed = Runtime.AcceptTerminalConnection(shownRequestId);
            else if (shownConnectionId != null) changed = Runtime.DisconnectTerminalConversation(shownConnectionId);
            else changed = Runtime.ResumeTerminalConnection(shownReconnectId);
            if (!changed) return;

            RefreshConnectionPresentation();
            ApplyInputInteraction();
            RefreshCommandPrompt();
            RefreshAnswerPrompt();
            QueueTranscriptScroll(toLatest: true);
        }
    }
}
