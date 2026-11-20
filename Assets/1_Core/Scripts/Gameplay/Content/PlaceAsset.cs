using System;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Place", fileName = "Place")]
    public sealed class PlaceAsset : PhysicalAsset
    {
        public override string BuiltinSchemaId => "Place";
        public ItemAsset[] items = Array.Empty<ItemAsset>();
        public PersonAsset[] people = Array.Empty<PersonAsset>();
        public PlaceAsset[] connections = Array.Empty<PlaceAsset>();
        protected override void Write(DataAssetBuilder b) { base.Write(b); b.List("items", items); b.List("people", people); b.List("connection", connections); }
    }
}
