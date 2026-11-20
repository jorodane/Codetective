using System;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Concept", fileName = "Concept")]
    public class ConceptAsset : WorldDataAsset
    {
        public override string BuiltinSchemaId => "Concept";
        [TextArea] public string description;
        public string[] features = Array.Empty<string>();
        protected override void Write(DataAssetBuilder b)
        { b.Set("description", Datum.Text(description ?? "")); b.Set("features", Datum.List(Array.ConvertAll(features ?? Array.Empty<string>(), Datum.Text))); }
    }
}
