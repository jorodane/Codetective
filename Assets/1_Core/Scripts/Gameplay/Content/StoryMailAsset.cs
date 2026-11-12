using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Story/Mail", fileName = "StoryMail")]
    public sealed class StoryMailAsset : ScriptableObject { public StoryMail message = new StoryMail(); }
}
