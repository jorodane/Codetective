using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Item", fileName = "Item")]
    public class ItemAsset : PhysicalAsset
    {
        public override string BuiltinSchemaId => "Item";
        public PersonAsset owner;
        protected override void Write(DataAssetBuilder b) { base.Write(b); b.Reference("owner", owner); }
    }
}
