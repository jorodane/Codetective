using System;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Operations/Scenario", fileName = "KernelScenario")]
    public sealed class KernelScenarioDefinition : ScriptableObject
    {
        [Min(0.1f)] public float secondsPerGameMinute = 2f;
        public bool loadDailySave = true;
        public ConsultationLine[] consultation = Array.Empty<ConsultationLine>();
        public EntityDefinition[] additionalObjects = Array.Empty<EntityDefinition>();

        public KernelRuntime CreateRuntime()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            if (consultation.Length > 0) { runtime.Consultation.Clear(); runtime.Consultation.AddRange(consultation); }
            foreach (EntityDefinition definition in additionalObjects) runtime.Register(CreateEntity(definition));
            return runtime;
        }

        private static Entity CreateEntity(EntityDefinition definition)
        {
            var entity = new Entity(definition.code, definition.label, definition.kind)
            {
                Aliases = definition.aliases,
                Access = new AccessRule { Facts = definition.requiredFacts, Authorities = definition.requiredAuthorities,
                    ForbiddenFacts = definition.forbiddenFacts }
            };
            foreach (ActionDefinition action in definition.actions)
            {
                ActionDefinition captured = action;
                entity.Operations.Add(new Operation
                {
                    Code = action.code, Label = action.label, Aliases = action.aliases, ReturnKind = action.result.kind, Property = action.property,
                    Parameters = Array.ConvertAll(action.parameters, p => new Parameter(p.name, p.kind)), Minutes = action.minutes,
                    Access = new AccessRule { Facts = action.requiredFacts, Authorities = action.requiredAuthorities,
                        ForbiddenFacts = action.forbiddenFacts, RequiredData = action.requiredData, NeedsReadyThread = action.needsReadyThread },
                    Invoke = c => { c.Runtime.Facts.UnionWith(captured.unlockFacts); c.Runtime.Authorities.UnionWith(captured.grantAuthorities); return captured.result.Copy(); }
                });
            }
            foreach (EntityDefinition child in definition.children) entity.Children.Add(child.code, CreateEntity(child));
            return entity;
        }
    }

    [Serializable] public sealed class EntityDefinition
    {
        public string code;
        public string label;
        public DataKind kind = DataKind.Place;
        public string[] aliases = Array.Empty<string>();
        public string[] requiredFacts = Array.Empty<string>();
        public string[] requiredAuthorities = Array.Empty<string>();
        public string[] forbiddenFacts = Array.Empty<string>();
        public ActionDefinition[] actions = Array.Empty<ActionDefinition>();
        public EntityDefinition[] children = Array.Empty<EntityDefinition>();
    }
    [Serializable] public sealed class InputDefinition { public string name; public DataKind kind = DataKind.Any; }
    [Serializable] public sealed class ActionDefinition
    {
        public string code;
        public string label;
        public string[] aliases = Array.Empty<string>();
        public bool property;
        public bool needsReadyThread;
        public InputDefinition[] parameters = Array.Empty<InputDefinition>();
        public float minutes;
        public string[] requiredFacts = Array.Empty<string>();
        public string[] requiredAuthorities = Array.Empty<string>();
        public string[] forbiddenFacts = Array.Empty<string>();
        public DataKind requiredData;
        public string[] unlockFacts = Array.Empty<string>();
        public string[] grantAuthorities = Array.Empty<string>();
        public Datum result = new Datum();
    }
}
