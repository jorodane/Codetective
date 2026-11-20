using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Relationship", fileName = "Relationship")]
    public sealed class RelationshipAsset : WorldDataAsset
    {
        public override string BuiltinSchemaId => "Relationship";
        public string category;
        [TextArea] public string description;
        protected override void Write(DataAssetBuilder b) { b.Set("category", Datum.Text(category ?? "")); b.Set("description", Datum.Text(description ?? "")); }
    }
}
