using System;
using System.Linq;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Text Table", fileName = "ContentTexts")]
    public sealed class ContentTextTableAsset : ScriptableObject
    {
        public string tableId = "texts_" + Guid.NewGuid().ToString("N");
        public TextTranslation[] entries = Array.Empty<TextTranslation>();
        public static void Install(KernelRuntime runtime, params ContentTextTableAsset[] tables)
        {
            runtime.Texts.Install((tables ?? Array.Empty<ContentTextTableAsset>()).Where(t => t != null).Distinct().SelectMany(t => t.entries));
        }
    }
    [Serializable] public sealed class FieldTextKey
    {
        [Tooltip("Text field code, e.g. description, occupation, content. This is not a command alias.")]
        public string field;
        public string key;
    }
}
