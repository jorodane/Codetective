using System;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Physical", fileName = "Physical")]
    public class PhysicalAsset : ConceptAsset
    {
        public override string BuiltinSchemaId => "Physical";
        public PhotoAsset portrait;
        public EventAsset[] history = Array.Empty<EventAsset>();
        public RelationLink[] relations = Array.Empty<RelationLink>();
        protected override void Write(DataAssetBuilder b)
        { base.Write(b); b.Reference("portrait", portrait); b.List("history", history); b.Relations("relations", relations); }
    }
}
