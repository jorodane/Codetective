using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Story/Clinic Day Plan", fileName = "ClinicDayPlan")]
    public sealed class ClinicDayPlanAsset : ScriptableObject
    {
        public ClinicDayPlan plan = new ClinicDayPlan();
    }
}
