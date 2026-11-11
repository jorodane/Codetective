using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Codetective.Terminal
{
    /// <summary>Temporarily gives flat-layout controls the coordinates of a curved-screen hit.</summary>
    internal readonly struct CrtPointerProjection : IDisposable
    {
        private readonly PointerEventData data;
        private readonly Vector2 position;
        private readonly Vector2 pressPosition;
        private readonly Vector2 delta;
        private readonly RaycastResult currentRaycast;
        private readonly RaycastResult pressRaycast;

        public CrtPointerProjection(PointerEventData data, CrtScreen screen)
        {
            this.data = data;
            position = data.position;
            pressPosition = data.pressPosition;
            delta = data.delta;
            currentRaycast = data.pointerCurrentRaycast;
            pressRaycast = data.pointerPressRaycast;
            if (screen == null || !screen.IsCurved)
                return;

            Camera camera = data.pressEventCamera != null ? data.pressEventCamera : data.enterEventCamera;
            if (camera == null)
            {
                var canvas = screen.GetComponentInParent<Canvas>();
                camera = canvas != null ? canvas.worldCamera : null;
            }
            data.position = Project(screen, position, camera);
            data.pressPosition = Project(screen, pressPosition, camera);
            data.delta = data.position - Project(screen, position - delta, camera);

            RaycastResult raycast = currentRaycast;
            raycast.screenPosition = Project(screen, raycast.screenPosition, camera);
            data.pointerCurrentRaycast = raycast;
            raycast = pressRaycast;
            raycast.screenPosition = Project(screen, raycast.screenPosition, camera);
            data.pointerPressRaycast = raycast;
        }

        private static Vector2 Project(CrtScreen screen, Vector2 point, Camera camera)
        {
            // Extend the mathematical surface for drags beyond the monitor's edge.
            return screen.TryProjectPointer(point, camera, false, out Vector2 flat, out _) ? flat : point;
        }

        public void Dispose()
        {
            data.position = position;
            data.pressPosition = pressPosition;
            data.delta = delta;
            data.pointerCurrentRaycast = currentRaycast;
            data.pointerPressRaycast = pressRaycast;
        }
    }
}
