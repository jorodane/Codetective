using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/World", fileName = "World")]
    public sealed class WorldDefinition : ScriptableObject
    {
        public string worldId = "world_" + Guid.NewGuid().ToString("N");
        public TimeAsset currentTime;
        public bool includeClinicDefaults = true;
        public EntitySchemaAsset[] schemas = Array.Empty<EntitySchemaAsset>();
        public EntityInstanceAsset[] objects = Array.Empty<EntityInstanceAsset>();
        public EventAsset[] occurredEvents = Array.Empty<EventAsset>();
        public CurrentInformationAsset initialInformation;
        public EntityInstanceAsset[] Roots => objects.Concat(occurredEvents).Concat(currentTime != null ? new EntityInstanceAsset[] { currentTime } : Array.Empty<EntityInstanceAsset>())
            .Concat(initialInformation != null ? initialInformation.Objects : Array.Empty<EntityInstanceAsset>()).Where(a => a != null).Distinct().ToArray();
        public EntityInstanceAsset[] AllObjects => EntityAssetCompiler.ReferencedInstances(schemas, Roots);
        public void ConfigureClock(KernelRuntime runtime) { if (currentTime != null) runtime.ConfigureInitialClock(currentTime.Timestamp); }
        public void ApplyInformation(KernelRuntime runtime)
        {
            foreach (EventAsset item in occurredEvents) { if (item == null) throw new CommandException("발생한 사건 목록에 빈 에셋이 있습니다."); runtime.RecordEvent(item.instanceId); }
            initialInformation?.Apply(runtime);
        }
        public KernelRuntime CreateRuntime()
        {
            KernelRuntime runtime = includeClinicDefaults ? ClinicScenario.Create() : new KernelRuntime();
            ConfigureClock(runtime); EntityAssetCompiler.Install(runtime, schemas, Roots);
            ApplyInformation(runtime); runtime.CaptureScenarioEntityDefaults(); return runtime;
        }
    }
}
