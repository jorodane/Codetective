using System;
using System.Text;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>Reusable viewer for acquired data. No world field getters are used by this view.</summary>
    public sealed class DataViewerView : MonoBehaviour
    {
        private Text body;
        private Image picture;
        private Func<string, Sprite> resolvePhoto;
        public void Initialize(Text text, Image image, Func<string, Sprite> photos)
        { body = text; picture = image; resolvePhoto = photos; body.supportRichText = true; }
        public void Show(DataViewerModel model, string acquisition, Sprite fallback = null)
        {
            var text = new StringBuilder();
            text.AppendLine("<size=28>" + Escape(model.Name) + "</size>");
            text.AppendLine(Escape(model.Title));
            text.AppendLine("\n" + Escape(acquisition));
            foreach (DataViewerField field in model.Fields)
            {
                text.AppendLine("\n<color=#83B69A>" + Escape(field.Label) + "</color>");
                text.AppendLine(Escape(field.Known ? field.DisplayValue : "[미확인]"));
            }
            if (model.Information.Length > 0)
            {
                text.AppendLine("\n<color=#83B69A>획득 정보 / 이력</color>");
                foreach (string record in model.InformationText) text.AppendLine("\n" + Escape(record));
            }
            body.text = text.ToString().TrimEnd();
            picture.sprite = !string.IsNullOrEmpty(model.PhotoId) ? resolvePhoto?.Invoke(model.PhotoId) : fallback;
            picture.enabled = picture.sprite != null;
        }
        public void Clear(string message)
        { body.text = Escape(message); picture.sprite = null; picture.enabled = false; }
        private static string Escape(string value) => (value ?? "").Replace("<", "‹").Replace(">", "›");
    }
}
