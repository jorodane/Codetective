using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Tool", fileName = "Tool")]
    public sealed class ToolAsset : ItemAsset { public override string BuiltinSchemaId => "Tool"; }
}
