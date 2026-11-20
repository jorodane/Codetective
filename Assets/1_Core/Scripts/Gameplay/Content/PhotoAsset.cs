using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Photo", fileName = "Photo")]
    public sealed class PhotoAsset : WorldDataAsset
    {
        public override string BuiltinSchemaId => "Photo";
        public Sprite image;
        public TimeAsset takenAt;
        [TextArea] public string description;
        protected override void Write(DataAssetBuilder b)
        { b.Set("image", image != null ? Datum.Text(instanceId) : Datum.Empty); b.Reference("time", takenAt); b.Set("description", Datum.Text(description ?? "")); }
    }
}
