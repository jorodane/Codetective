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
        public CommandLocale commandLanguage = CommandLocale.Korean;
        public ConsultationLine[] consultation = Array.Empty<ConsultationLine>();
        public EntitySchemaAsset[] schemas = Array.Empty<EntitySchemaAsset>();
        public EntityInstanceAsset[] instances = Array.Empty<EntityInstanceAsset>();
        public EntityDefinition[] additionalObjects = Array.Empty<EntityDefinition>();
        public StoryMailAsset[] mail = Array.Empty<StoryMailAsset>();
        public string storyStartFact = "";

        public KernelRuntime CreateRuntime()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            runtime.Language = commandLanguage;
            if (consultation.Length > 0) { runtime.Consultation.Clear(); runtime.Consultation.AddRange(consultation); }
            EntityAssetCompiler.Install(runtime, schemas, instances, additionalObjects);
            foreach (StoryMailAsset item in mail) if (item != null) runtime.RegisterMail(item.message);
            runtime.StoryStartFact = storyStartFact;
            return runtime;
        }
    }

    [Serializable] public sealed class EntityDefinition
    {
        public string code;
        public string label;
        public DataKind kind = DataKind.Place;
        public string[] aliases = Array.Empty<string>();
        public LocalizedCommandName[] names = Array.Empty<LocalizedCommandName>();
        public string[] requiredFacts = Array.Empty<string>();
        public string[] requiredAuthorities = Array.Empty<string>();
        public string[] forbiddenFacts = Array.Empty<string>();
        public ActionDefinition[] actions = Array.Empty<ActionDefinition>();
        public EntityDefinition[] children = Array.Empty<EntityDefinition>();
    }
    [Serializable] public sealed class InputDefinition
    {
        public string name;
        public DataKind kind = DataKind.Any;
        public EntitySchemaAsset referenceType;
    }
    [Serializable] public sealed class ActionDefinition
    {
        public string code;
        public string label;
        public string[] aliases = Array.Empty<string>();
        public LocalizedCommandName[] names = Array.Empty<LocalizedCommandName>();
        public bool property;
        public bool overrides;
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
        [Tooltip("Return the target instance's field instead of a constant result.")]
        public string returnField;
        public EntityInstanceAsset returnInstance;
        public FieldWriteDefinition[] fieldWrites = Array.Empty<FieldWriteDefinition>();
        [Tooltip("Change available fields or conversation functions on the target instance after this action.")]
        public MemberToggle[] memberChanges = Array.Empty<MemberToggle>();
    }
}
