using System;
using System.Collections.Generic;
using UnityEngine;

namespace Codetective.CameraViews
{
    [CreateAssetMenu(menuName = "Codetective/Camera/Angle Set", fileName = "CameraAngles")]
    public sealed class CameraAngleSet : ScriptableObject
    {
        [SerializeField] private List<CameraAnglePreset> angles = new List<CameraAnglePreset>();
        [SerializeField, Min(0)] private int initialIndex;

        public IReadOnlyList<CameraAnglePreset> Angles => angles;
        public int Count => angles.Count;
        public CameraAnglePreset Get(int index) => index >= 0 && index < Count ? angles[index] : null;
        public int IndexOf(CameraAnglePreset preset) => preset != null ? angles.IndexOf(preset) : -1;

        public int InitialIndex
        {
            get
            {
                if (Get(initialIndex) != null)
                    return initialIndex;
                return NextIndex(-1, 1);
            }
        }

        public int FindId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return -1;

            int found = -1;
            for (int i = 0; i < Count; i++)
            {
                if (angles[i] == null || !string.Equals(angles[i].Id, id.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (found >= 0)
                    return -1; // An ambiguous ID must not silently select a different angle.
                found = i;
            }
            return found;
        }

        public int NextIndex(int from, int direction)
        {
            if (Count == 0)
                return -1;
            direction = direction < 0 ? -1 : 1;
            if (from < 0 || from >= Count)
                from = direction > 0 ? -1 : 0;
            for (int step = 1; step <= Count; step++)
            {
                int index = ((from + direction * step) % Count + Count) % Count;
                if (angles[index] != null)
                    return index;
            }
            return -1;
        }
    }
}
