using System.Collections;
using Codetective.CameraViews;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    /// <summary>Swap the seated visitor while the room is hidden; preserve the runtime's arrival/closing/departure acknowledgements.</summary>
    public sealed class ClinicVisitPresentation : MonoBehaviour
    {
        [SerializeField] private GameObject patientPrefab;
        [SerializeField] private Transform seatAnchor;
        [Tooltip("The hidden, posed scene character used to seat each visitor.")]
        [SerializeField] private Transform seatPoseReference;
        [SerializeField, Min(0.1f)] private float closingSeconds = 2.4f;
        private GameObject actor;
        private Material standInMaterial;
        private ProfileCatalog profiles;
        private SceneTransitionController transitions;
        private string shownProfile;
        public bool IsRunning { get; private set; }
        public string ShownProfileId => shownProfile;
        private Vector3 Seat => seatPoseReference != null ? seatPoseReference.position : seatAnchor != null ? seatAnchor.position : new Vector3(-0.35f, 0.1f, 0.13f);
        public void Configure(ProfileCatalog catalog, SceneTransitionController presentation) { profiles = catalog; transitions = presentation; }
        public void Initialize(KernelRuntime runtime)
        {
            if (seatPoseReference != null) seatPoseReference.gameObject.SetActive(false);
            AppointmentProgress visit = runtime.CurrentAppointment;
            if (visit != null && visit.phase != VisitPhase.Arriving && visit.phase != VisitPhase.Departing) Show(visit.profileId);
        }
        private void Show(string id)
        {
            if (actor != null && shownProfile == id) { actor.SetActive(true); return; }
            if (actor != null) { actor.SetActive(false); ProfileModelUtility.Dispose(actor); }
            CharacterProfileAsset profile = profiles != null ? profiles.Character(id) : null;
            GameObject prefab = profile != null && profile.modelPrefab != null ? profile.modelPrefab : patientPrefab;
            if (prefab != null) actor = Instantiate(prefab, transform);
            else
            {
                actor = new GameObject("UnassignedPatientModel"); actor.transform.SetParent(transform, false);
                if (standInMaterial == null) { standInMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")); standInMaterial.color = new Color(0.22f, 0.25f, 0.23f); }
                Part("Torso", PrimitiveType.Capsule, new Vector3(0, 0.74f, 0), new Vector3(0.43f, 0.30f, 0.30f));
                Part("Head", PrimitiveType.Sphere, new Vector3(0, 1.17f, 0), new Vector3(0.24f, 0.28f, 0.24f));
            }
            shownProfile = id; actor.name = "Patient_" + id;
            Vector3 offset = profile != null ? profile.seatOffset : Vector3.zero;
            Vector3 seat = Seat + offset;
            Quaternion rotation = seatPoseReference != null ? seatPoseReference.rotation : Quaternion.Euler(profile != null ? profile.modelEuler : new Vector3(0, 180, 0));
            actor.transform.SetPositionAndRotation(seat, rotation);
            if (profile != null) actor.transform.localScale = Vector3.Scale(actor.transform.localScale, profile.modelScale);
            ProfileModelUtility.Prepare(actor);
            if ((profile == null || profile.applySeatedPose) && !ProfileModelUtility.CopySeatPose(actor, seatPoseReference, offset))
                ProfileModelUtility.Seat(actor, profile, seat);
            actor.SetActive(true);
        }
        private void Part(string name, PrimitiveType type, Vector3 position, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(type); part.name = name; part.transform.SetParent(actor.transform, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = standInMaterial; part.GetComponent<Collider>().enabled = false;
        }
        public IEnumerator Play(KernelRuntime runtime, AppointmentProgress visit)
        {
            IsRunning = true;
            try
            {
                if (visit.phase == VisitPhase.Closing)
                    yield return SceneTransitionController.Hold(closingSeconds);
                else if (transitions != null)
                    yield return transitions.Play(transitions.Settings?.visit, TransitionStyle.TerminalFade, () =>
                    { if (visit.phase == VisitPhase.Arriving) Show(visit.profileId); else if (actor != null) actor.SetActive(false); });
                else if (visit.phase == VisitPhase.Arriving) Show(visit.profileId);
                else if (actor != null) actor.SetActive(false);
                runtime.AdvanceVisitPresentation(visit.id, visit.phase);
            }
            finally { IsRunning = false; }
        }
        private void OnDestroy() { ProfileModelUtility.Dispose(actor); ProfileModelUtility.Dispose(standInMaterial); }
    }
}
