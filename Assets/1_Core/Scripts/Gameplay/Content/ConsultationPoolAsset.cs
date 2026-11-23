using System;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Story/Consultation Pool", fileName = "ConsultationPool")]
    public sealed class ConsultationPoolAsset : ScriptableObject
    {
        public string poolId;
        public ConsultationEpisode[] episodes = Array.Empty<ConsultationEpisode>();
    }
}
