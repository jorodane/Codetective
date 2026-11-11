using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [AddComponentMenu("Codetective/CRT Graphic Raycaster")]
    public sealed class CrtGraphicRaycaster : GraphicRaycaster
    {
        private CrtScreen screen;

        public override void Raycast(PointerEventData eventData, List<RaycastResult> results)
        {
            if (screen == null)
                screen = GetComponentInChildren<CrtScreen>(true);
            if (screen == null || !screen.IsCurved || eventCamera == null)
            {
                base.Raycast(eventData, results);
                return;
            }

            Vector2 original = eventData.position;
            if (!screen.TryProjectPointer(original, eventCamera, true, out Vector2 flat, out Vector3 hit))
                return;

            int firstResult = results.Count;
            eventData.position = flat;
            try
            {
                // Retain uGUI sorting, clipping masks, and per-Graphic raycast filters.
                base.Raycast(eventData, results);
            }
            finally
            {
                eventData.position = original;
            }

            float distance = Vector3.Distance(eventCamera.ScreenPointToRay(original).origin, hit);
            Vector3 normal = screen.WorldNormal(hit);
            for (int i = firstResult; i < results.Count; i++)
            {
                RaycastResult result = results[i];
                result.screenPosition = original;
                result.worldPosition = hit;
                result.worldNormal = normal;
                result.distance = distance;
                results[i] = result;
            }
        }
    }
}
