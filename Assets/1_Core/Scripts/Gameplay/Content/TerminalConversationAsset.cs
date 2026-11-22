using System;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Story/Terminal Conversation", fileName = "TerminalConversation")]
    public sealed class TerminalConversationAsset : ScriptableObject
    {
        public TerminalConversationDefinition conversation = new TerminalConversationDefinition();
        public ContentTextTableAsset[] textTables = Array.Empty<ContentTextTableAsset>();
    }
}
