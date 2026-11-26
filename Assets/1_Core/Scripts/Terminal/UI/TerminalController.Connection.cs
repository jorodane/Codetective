using System;
using System.Collections.Generic;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed partial class TerminalController
    {
        private static readonly Color ConnectionBackground = new Color(0.027f, 0.054f, 0.042f);
        private static readonly Color RequestColor = new Color(0.91f, 0.79f, 0.49f);
        private readonly List<(RectTransform rect, float left)> consoleLeftEdges = new List<(RectTransform, float)>();
        private readonly List<(RectTransform rect, float right)> candidateRightEdges = new List<(RectTransform, float)>();
        private RectTransform contactPanel, requestPanel;
        private Text contactStatus, contactName, requestTitle, requestName, acceptLabel, disconnectLabel, reconnectLabel;
        private Image contactPortrait;
        private Button acceptConnection, disconnectConnection, reconnectConnection;
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
            string reconnect = incoming == null ? Runtime.TerminalReconnectId : null;
            if (contactPanel == null && (!pageVisible || connected == null && incoming == null && reconnect == null || !BuildConnectionView())) return;

            bool split = connected != null;
            if (consoleSplit != split) LayoutConnectedConsole(split);
            contactPanel.gameObject.SetActive(pageVisible && split);
            requestPanel.gameObject.SetActive(pageVisible && incoming != null);
            reconnectConnection.gameObject.SetActive(pageVisible && reconnect != null);
            acceptConnection.interactable = disconnectConnection.interactable = reconnectConnection.interactable = interactionEnabled;

            int nextPortraitRevision = contactHost != null ? contactHost.ProfilePageRevision : 0;
            string locale = Runtime.DisplayLanguage;
            if (shownConnectionId == connected && shownRequestId == incoming && shownReconnectId == reconnect && connectionLocale == locale &&
                contactRevision == Runtime.Revision && portraitRevision == nextPortraitRevision) return;
            shownConnectionId = connected; shownRequestId = incoming; shownReconnectId = reconnect; connectionLocale = locale;
            contactRevision = Runtime.Revision; portraitRevision = nextPortraitRevision;

            bool english = locale == "en";
            if (incoming != null)
            {
                requestPanel.SetAsLastSibling();
                requestTitle.text = english ? "Incoming connection" : "연결 요청";
                requestName.text = Runtime.ContactForTerminalConversation(incoming).Name;
                acceptLabel.text = english ? "Accept" : "수락";
            }
            if (reconnect != null) reconnectLabel.text = english ? "Reconnect" : "다시 연결";
            if (!split) return;
            TerminalContact contact = Runtime.ContactForTerminalConversation(connected);
            contactName.text = contact.Name;
            contactStatus.text = english ? "Connected" : "연결됨";
            disconnectLabel.text = english ? "Disconnect" : "연결 끊기";
            contactPortrait.sprite = string.IsNullOrEmpty(contact.ProfileId) || contactHost == null ? null : contactHost.PortraitFor(contact.ProfileId);
            contactPortrait.gameObject.SetActive(contactPortrait.sprite != null);
        }

        private bool BuildConnectionView()
        {
            RectTransform field = (RectTransform)input.transform;
            if (!(field.parent is RectTransform page) || transcriptScroll.transform.parent != page || completionPanel.transform.parent != page) return false;
            float width = field.rect.width;
            if (width <= 0 || page.rect.height <= 0) return false;

            contactHost = GetComponent<MonitorPageHost>();
            contactWidth = Mathf.Round(width * 0.25f);
            foreach (RectTransform rect in new[] { field, (RectTransform)transcriptScroll.transform, (RectTransform)completionPanel.transform,
                page.Find("LogHeading") as RectTransform, page.Find("Help") as RectTransform })
                if (rect != null) consoleLeftEdges.Add((rect, rect.offsetMin.x));
            foreach (RectTransform rect in completionPanel.transform) candidateRightEdges.Add((rect, rect.offsetMax.x));
            foreach (Text label in completionLabels)
                if (label != null && label.rectTransform.parent != completionPanel.transform && label.rectTransform.anchorMax.x == label.rectTransform.anchorMin.x)
                    candidateRightEdges.Add((label.rectTransform, label.rectTransform.offsetMax.x));

            Font font = input.textComponent.font;
            RectTransform heading = page.Find("LogHeading") as RectTransform;
            float left = field.anchoredPosition.x, top = heading != null ? heading.anchoredPosition.y : -86;
            float height = page.rect.height + top - 20;
            contactPanel = ConnectionBox("TerminalContact", page, new Vector2(left, top), new Vector2(contactWidth, height));
            contactStatus = MonitorWidgets.Label(contactPanel, font, new Vector2(20, -20), new Vector2(contactWidth - 40, 28), "", 20);
            RectTransform rule = ConnectionBox("ContactRule", contactPanel, new Vector2(20, -54), new Vector2(contactWidth - 40, 2));
            Image line = rule.GetComponent<Image>(); line.color = MonitorWidgets.SelectedButtonColor; line.raycastTarget = false;
            contactName = ContactLabel(contactPanel, font, new Vector2(20, -72), new Vector2(contactWidth - 40, 84), 32);
            var photo = MonitorWidgets.Rect("ContactPortrait", contactPanel, new Vector2(20, -174), new Vector2(contactWidth - 40, 250));
            contactPortrait = photo.gameObject.AddComponent<Image>();
            contactPortrait.preserveAspect = true; contactPortrait.raycastTarget = false;
            photo.gameObject.AddComponent<CrtMeshEffect>();
            disconnectConnection = MonitorWidgets.Button(contactPanel, font, new Vector2(20, -height + 66), new Vector2(contactWidth - 40, 44), "", Disconnect);
            disconnectConnection.name = "Disconnect";
            disconnectLabel = disconnectConnection.GetComponentInChildren<Text>();

            requestPanel = ConnectionBox("TerminalConnectionRequest", page, new Vector2(left, top), new Vector2(width, height));
            RectTransform card = ConnectionBox("IncomingCaller", requestPanel, new Vector2(width * 0.5f - 235, -height * 0.5f + 124), new Vector2(470, 248));
            card.GetComponent<Image>().color = new Color(0.054f, 0.108f, 0.08f);
            requestTitle = MonitorWidgets.Label(card, font, new Vector2(24, -26), new Vector2(422, 34), "", 25);
            requestTitle.color = RequestColor;
            requestName = ContactLabel(card, font, new Vector2(24, -72), new Vector2(422, 75), 38);
            acceptConnection = MonitorWidgets.Button(card, font, new Vector2(24, -170), new Vector2(422, 52), "", AcceptConnection);
            acceptConnection.name = "AcceptConnection";
            acceptConnection.image.color = MonitorWidgets.SelectedButtonColor;
            acceptLabel = acceptConnection.GetComponentInChildren<Text>();

            reconnectConnection = MonitorWidgets.Button(page, font, new Vector2(left + width - 208, top + 4), new Vector2(208, 36), "", ResumeConnection);
            reconnectConnection.name = "Reconnect";
            reconnectLabel = reconnectConnection.GetComponentInChildren<Text>();
            return true;
        }

        private static Text ContactLabel(RectTransform panel, Font font, Vector2 position, Vector2 size, int fontSize)
        {
            Text label = MonitorWidgets.Label(panel, font, position, size, "", fontSize);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 20; label.resizeTextMaxSize = fontSize;
            return label;
        }

        private static RectTransform ConnectionBox(string name, Transform parent, Vector2 position, Vector2 size)
        {
            RectTransform rect = MonitorWidgets.Rect(name, parent, position, size);
            rect.gameObject.AddComponent<Image>().color = ConnectionBackground;
            rect.gameObject.AddComponent<CrtMeshEffect>();
            return rect;
        }

        private void LayoutConnectedConsole(bool split)
        {
            consoleSplit = split;
            float inset = split ? contactWidth + 14 : 0;
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

        private void AcceptConnection()
        { if (pageVisible && interactionEnabled && Runtime.AcceptTerminalConnection(shownRequestId)) RefreshAfterConnectionAction(); }
        private void ResumeConnection()
        { if (pageVisible && interactionEnabled && Runtime.ResumeTerminalConnection(shownReconnectId)) RefreshAfterConnectionAction(); }
        private void Disconnect()
        { if (pageVisible && interactionEnabled && Runtime.DisconnectTerminalConversation(shownConnectionId)) RefreshAfterConnectionAction(); }

        private void RefreshAfterConnectionAction()
        {
            RefreshConnectionPresentation();
            ApplyInputInteraction();
            RefreshCommandPrompt();
            RefreshAnswerPrompt();
            QueueTranscriptScroll(toLatest: true);
        }
    }
}
