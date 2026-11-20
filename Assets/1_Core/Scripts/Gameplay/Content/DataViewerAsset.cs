using System;
using System.Linq;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Viewer", fileName = "DataViewer")]
    public sealed class DataViewerAsset : ScriptableObject
    {
        public string viewerId = "viewer_" + Guid.NewGuid().ToString("N");
        public DataViewerKind layout;
        public string title;
        [Tooltip("Field codes in display order. Empty uses all accessible fields. Schema inheritance supplies the fallback viewer.")]
        public string[] fields = Array.Empty<string>();
        public bool hideUnknown = true;
        public DataViewerDefinition Compile()
        {
            if (!Enum.IsDefined(typeof(DataViewerKind), layout) || fields == null || fields.Any(string.IsNullOrWhiteSpace) || fields.Distinct().Count() != fields.Length)
                throw new CommandException("Viewer의 레이아웃 또는 필드 목록이 유효하지 않습니다: " + name);
            return new DataViewerDefinition { kind = layout, title = title, fields = fields.ToArray(), hideUnknown = hideUnknown };
        }
    }
}
