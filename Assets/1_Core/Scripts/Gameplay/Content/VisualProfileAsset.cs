using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    public abstract class VisualProfileAsset : ScriptableObject
    {
        [Tooltip("Existing world identity when no Entity Instance is assigned.")]
        public string worldId;
        [Tooltip("The instance owns schema, name, personal fields and access rules. Its ID takes precedence over World Id.")]
        public EntityInstanceAsset entity;
        public GameObject modelPrefab;
        public Sprite photo;
        [TextArea] public string description;
        public StoryCondition availability = new StoryCondition();
        [Tooltip("Bake a missing photo in the Unity Editor; an assigned photo is never overwritten automatically.")]
        public bool captureMissingPhoto = true;
        public Vector3 modelScale = Vector3.one;
        public Vector3 modelEuler = new Vector3(0, 180, 0);
        public string Id => entity != null ? entity.instanceId : worldId;
    }
}
