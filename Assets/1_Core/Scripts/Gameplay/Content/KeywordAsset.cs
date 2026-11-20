using System;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Keyword", fileName = "Keyword")]
    public sealed class KeywordAsset : ConceptAsset
    {
        public override string BuiltinSchemaId => "Keyword";
        public ConceptAsset topic;
        public RelationLink[] relations = Array.Empty<RelationLink>();
        protected override void Write(DataAssetBuilder b) { base.Write(b); b.Reference("topic", topic); b.Relations("relations", relations); }
    }
}
