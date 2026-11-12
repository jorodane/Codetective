using System;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Operations/Entity Instance", fileName = "EntityInstance")]
    public sealed class EntityInstanceAsset : ScriptableObject
    {
        [Tooltip("Stable world identity. Reference aliases and save files use this ID.")]
        public string instanceId;
        public string displayName;
        public EntitySchemaAsset schema;
        public bool exposeRoot = true;
        public string[] aliases = Array.Empty<string>();
        public LocalizedCommandName[] names = Array.Empty<LocalizedCommandName>();
        public AccessDefinition access = new AccessDefinition();
        [Tooltip("Members owned only by this instance, in addition to its inherited schema members.")]
        public FieldDefinition[] fields = Array.Empty<FieldDefinition>();
        public ActionDefinition[] functions = Array.Empty<ActionDefinition>();
        public string[] disabledVariables = Array.Empty<string>();
        public string[] disabledFunctions = Array.Empty<string>();
        public InitialFieldDefinition[] initialValues = Array.Empty<InitialFieldDefinition>();
        [Tooltip("Containment links, independent of schema inheritance.")]
        public ChildInstanceDefinition[] children = Array.Empty<ChildInstanceDefinition>();
    }

    [Serializable] public sealed class InitialFieldDefinition
    {
        public string field;
        public Datum value = new Datum();
        public EntityInstanceAsset reference;
    }
    [Serializable] public sealed class ChildInstanceDefinition
    {
        public string name;
        public EntityInstanceAsset instance;
    }
}
