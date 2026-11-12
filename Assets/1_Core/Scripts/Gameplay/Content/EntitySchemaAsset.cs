using System;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Operations/Entity Schema", fileName = "EntitySchema")]
    public sealed class EntitySchemaAsset : ScriptableObject
    {
        public string schemaId;
        public string displayName;
        [Tooltip("Inherited fields, functions and access requirements remain available on this type.")]
        public EntitySchemaAsset parent;
        [Tooltip("Used when Parent is empty: Entity, Place, Person, Item, Document, etc.")]
        public string builtinParent = "Entity";
        public bool inheritKind = true;
        public DataKind kind = DataKind.Entity;
        public string[] aliases = Array.Empty<string>();
        public LocalizedCommandName[] names = Array.Empty<LocalizedCommandName>();
        public AccessDefinition access = new AccessDefinition();
        public FieldDefinition[] fields = Array.Empty<FieldDefinition>();
        public ActionDefinition[] functions = Array.Empty<ActionDefinition>();
    }

    [Serializable] public sealed class AccessDefinition
    {
        public string[] requiredFacts = Array.Empty<string>();
        public string[] requiredAuthorities = Array.Empty<string>();
        public string[] forbiddenFacts = Array.Empty<string>();
        public AccessRule Compile() => new AccessRule { Facts = requiredFacts, Authorities = requiredAuthorities, ForbiddenFacts = forbiddenFacts };
    }

    [Serializable] public sealed class FieldDefinition
    {
        public string code;
        public string label;
        public DataKind kind = DataKind.Text;
        public EntitySchemaAsset referenceType;
        public Datum defaultValue = new Datum();
        public bool writable;
        public bool overrides;
        public string[] aliases = Array.Empty<string>();
        public LocalizedCommandName[] names = Array.Empty<LocalizedCommandName>();
        public AccessDefinition access = new AccessDefinition();
    }

    [Serializable] public sealed class FieldWriteDefinition
    {
        public string field;
        [Tooltip("-1 uses Value / Reference; 0 and above use that function argument.")]
        public int argumentIndex = -1;
        public Datum value = new Datum();
        public EntityInstanceAsset reference;
    }
}
