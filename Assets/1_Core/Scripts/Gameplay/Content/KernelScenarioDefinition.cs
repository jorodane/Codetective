using System;
using System.Linq;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Operations/Scenario", fileName = "KernelScenario")]
    public sealed class KernelScenarioDefinition : ScriptableObject
    {
        [Min(0.1f)] public float secondsPerGameMinute = 2f;
        public CommandLocale commandLanguage = CommandLocale.Korean;
        [Tooltip("Empty uses the World's Display Language (or ko without a World). Independent of Command Language.")]
        public string displayLanguage = "";
        public ContentTextTableAsset[] textTables = Array.Empty<ContentTextTableAsset>();
        public TerminalConversationAsset[] terminalConversations = Array.Empty<TerminalConversationAsset>();
        public ConsultationLine[] consultation = Array.Empty<ConsultationLine>();
        public EntitySchemaAsset[] schemas = Array.Empty<EntitySchemaAsset>();
        public EntityInstanceAsset[] instances = Array.Empty<EntityInstanceAsset>();
        public WorldDefinition worldData;
        public EntityDefinition[] additionalObjects = Array.Empty<EntityDefinition>();
        public StoryMailAsset[] mail = Array.Empty<StoryMailAsset>();
        public NewspaperArticleAsset[] newspaper = Array.Empty<NewspaperArticleAsset>();
        public StoryEffects initialStoryState = new StoryEffects();
        public ConsultationPoolAsset[] consultationPools = Array.Empty<ConsultationPoolAsset>();
        public ClinicDayPlanAsset[] clinicDays = Array.Empty<ClinicDayPlanAsset>();
        public string storyStartFact = "";
        public PresentationSettings presentation;
        public ProfileCatalog profiles;
        public string[] initialDiscoveries = Array.Empty<string>();
        public ObservationDefinition[] initialObservations = Array.Empty<ObservationDefinition>();
        public WorldScheduleEntry[] worldEvents = Array.Empty<WorldScheduleEntry>();

        public KernelRuntime CreateRuntime()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            worldData?.ConfigureClock(runtime);
            runtime.Language = commandLanguage;
            runtime.DisplayLanguage = string.IsNullOrWhiteSpace(displayLanguage) ? worldData != null ? worldData.displayLanguage : "ko" : displayLanguage;
            ContentTextTableAsset.Install(runtime, textTables.Concat(worldData != null ? worldData.textTables : Array.Empty<ContentTextTableAsset>())
                .Concat(terminalConversations.Where(c => c != null).SelectMany(c => c.textTables)).Distinct().ToArray());
            if (consultation.Length > 0) { runtime.Consultation.Clear(); runtime.Consultation.AddRange(consultation); }
            profiles?.Validate();
            EntityInstanceAsset[] profileInstances = profiles != null ? profiles.All.Select(p => p.entity).Where(p => p != null).ToArray() : Array.Empty<EntityInstanceAsset>();
            EntityAssetCompiler.Install(runtime, schemas.Concat(worldData != null ? worldData.schemas : Array.Empty<EntitySchemaAsset>()).Distinct().ToArray(),
                instances.Concat(profileInstances).Concat(worldData != null ? worldData.Roots : Array.Empty<EntityInstanceAsset>()).Distinct().ToArray(), additionalObjects);
            profiles?.ApplyDescriptions(runtime);
            runtime.StoryStartFact = storyStartFact;
            runtime.ApplyStoryEffects(initialStoryState);
            runtime.RefreshInitialKnowledge();
            worldData?.ApplyInformation(runtime);
            foreach (NewspaperArticleAsset item in newspaper) if (item != null && !item.supplemental) runtime.RegisterNews(item.article);
            runtime.DeliverNewspaper();
            foreach (ConsultationPoolAsset pool in consultationPools ?? Array.Empty<ConsultationPoolAsset>())
                if (pool != null) runtime.RegisterConsultationPool(pool.poolId, pool.episodes);
            foreach (TerminalConversationAsset item in terminalConversations) if (item != null) runtime.RegisterTerminalConversation(item.conversation);
            foreach (ClinicDayPlanAsset item in clinicDays) if (item != null) runtime.RegisterClinicDay(item.plan);
            foreach (StoryMailAsset item in mail) if (item != null) runtime.RegisterMail(item.message);
            runtime.InitializeClinicDay(); runtime.InitializeDailyRecord();
            foreach (string id in initialDiscoveries ?? Array.Empty<string>()) runtime.Discover(id, "초기 작전 자료");
            foreach (ObservationDefinition report in initialObservations ?? Array.Empty<ObservationDefinition>()) runtime.Observe(report);
            foreach (WorldScheduleEntry entry in worldEvents ?? Array.Empty<WorldScheduleEntry>()) runtime.RegisterWorldEvent(entry);
            runtime.CaptureScenarioEntityDefaults();
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
        [SerializeReference] public EntityDefinition[] children = Array.Empty<EntityDefinition>();
    }
    [Serializable] public sealed class InputDefinition
    {
        public string name;
        public DataKind kind = DataKind.Any;
        public string typeName = "";
        public EntitySchemaAsset referenceType;
    }
    [Serializable] public sealed class ActionDefinition
    {
        public PlayerAccess playerAccess = PlayerAccess.Inherit;
        public StoryCondition story = new StoryCondition();
        public StoryEffects storyEffects = new StoryEffects();
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
        public bool dispatchToThread;
        public ObservationDefinition[] reports = Array.Empty<ObservationDefinition>();
        public string[] discoveries = Array.Empty<string>();
        public string[] incidents = Array.Empty<string>();
        public ActionResponseDefinition[] responses = Array.Empty<ActionResponseDefinition>();
    }
}
