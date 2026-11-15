using System.Collections;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    /// <summary>Visual transitions acknowledge the core visit state; time and the next patient wait for each transition.</summary>
    public sealed class ClinicVisitPresentation : MonoBehaviour
    {
        [SerializeField] private GameObject patientPrefab;
        [SerializeField] private Transform entryAnchor, seatAnchor, exitAnchor;
        [SerializeField, Min(0.1f)] private float entrySeconds = 1.2f;
        [SerializeField, Min(0.1f)] private float closingSeconds = 2.4f;
        [SerializeField, Min(0.1f)] private float exitSeconds = 1.2f;
        private GameObject actor;
        private Material standInMaterial;
        public bool IsRunning { get; private set; }
        private Vector3 Entry => entryAnchor != null ? entryAnchor.position : new Vector3(1.3f, 0.1f, 0.1f);
        private Vector3 Seat => seatAnchor != null ? seatAnchor.position : new Vector3(-0.35f, 0.1f, 0.13f);
        private Vector3 Exit => exitAnchor != null ? exitAnchor.position : Entry;

        public void Initialize(KernelRuntime runtime)
        {
            if (patientPrefab != null) actor = Instantiate(patientPrefab, transform);
            else
            {
                actor = new GameObject("PatientStandIn_ReplaceWithCharacterPrefab"); actor.transform.SetParent(transform, false);
                standInMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                standInMaterial.color = new Color(0.22f, 0.25f, 0.23f);
                Part("Torso", PrimitiveType.Capsule, new Vector3(0, 0.74f, 0), new Vector3(0.43f, 0.30f, 0.30f));
                Part("Head", PrimitiveType.Sphere, new Vector3(0, 1.17f, 0), new Vector3(0.24f, 0.28f, 0.24f));
            }
            actor.transform.SetPositionAndRotation(Seat, Quaternion.Euler(0, 180, 0));
            foreach (Collider collider in actor.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody body in actor.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            actor.SetActive(runtime.CurrentAppointment != null && runtime.CurrentAppointment.phase != VisitPhase.Arriving);
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
            if (visit.phase == VisitPhase.Arriving)
            {
                actor.name = "Patient_" + visit.profileId; actor.SetActive(true);
                yield return Move(Entry, Seat, entrySeconds);
            }
            else if (visit.phase == VisitPhase.Closing)
            {
                actor.transform.position = Seat; actor.SetActive(true);
                yield return Hold(closingSeconds);
            }
            else if (visit.phase == VisitPhase.Departing)
            {
                actor.SetActive(true); yield return Move(Seat, Exit, exitSeconds); actor.SetActive(false);
            }
            runtime.AdvanceVisitPresentation(visit.id, visit.phase);
            IsRunning = false;
        }
        private IEnumerator Move(Vector3 from, Vector3 to, float seconds)
        {
            for (float time = 0; time < seconds; time += Time.deltaTime)
            { actor.transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, time / seconds)); yield return null; }
            actor.transform.position = to;
        }
        private static IEnumerator Hold(float seconds)
        { for (float time = 0; time < seconds; time += Time.deltaTime) yield return null; }
        private void OnDestroy() { if (actor != null) Destroy(actor); if (standInMaterial != null) Destroy(standInMaterial); }
    }
}
