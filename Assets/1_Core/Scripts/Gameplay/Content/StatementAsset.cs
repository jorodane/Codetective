using System;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Statement", fileName = "Statement")]
    public sealed class StatementAsset : WorldDataAsset
    {
        public override string BuiltinSchemaId => "Statement";
        public PersonAsset speaker;
        public ConceptAsset topic;
        public TimeAsset spokenAt;
        [TextArea] public string content;
        public PhysicalAsset[] subjects = Array.Empty<PhysicalAsset>();
        protected override void Write(DataAssetBuilder b)
        { b.Reference("speaker", speaker); b.Reference("topic", topic); b.Reference("time", spokenAt); b.Set("content", Datum.Text(content ?? "")); b.Set("description", Datum.Text(content ?? "")); b.List("subjects", subjects); }
    }
}
