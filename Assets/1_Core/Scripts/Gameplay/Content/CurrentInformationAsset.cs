using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Current Information", fileName = "CurrentInformation")]
    public sealed class CurrentInformationAsset : ScriptableObject
    {
        public string informationId = "information_" + Guid.NewGuid().ToString("N");
        public InitialDiscovery[] discoveries = Array.Empty<InitialDiscovery>();
        public InformationEntry[] entries = Array.Empty<InformationEntry>();
        public InferredInformation[] inferences = Array.Empty<InferredInformation>();
        public InitialReference[] references = Array.Empty<InitialReference>();
        public IEnumerable<EntityInstanceAsset> Objects => discoveries.Select(d => d.target).Concat(entries.SelectMany(e => new[] { e.subject, e.sourceObject, e.keyword, e.referenceValue }))
            .Concat(references.Select(r => r.target)).Where(a => a != null).Distinct();
        public void Apply(KernelRuntime runtime)
        {
            foreach (InitialDiscovery discovery in discoveries)
            {
                if (discovery.target == null) throw new CommandException("초기 발견 대상이 비어 있습니다.");
                runtime.Discover(discovery.target.instanceId, discovery.source, discovery.observeFields, discovery.listEvidence);
            }
            foreach (InferredInformation inference in inferences)
                runtime.CreateInformation(inference.id, inference.name, inference.content, inference.kind, inference.type, inference.source);
            foreach (InformationEntry entry in entries)
                runtime.Observe(new ObservationDefinition { subjectId = entry.subject != null ? entry.subject.instanceId : "", field = entry.field,
                    sourceId = entry.sourceObject != null ? entry.sourceObject.instanceId : "", source = entry.source, sourceKey = entry.sourceKey,
                    keywordId = entry.keyword != null ? entry.keyword.instanceId : "", statement = entry.statement, statementKey = entry.statementKey, kind = entry.kind,
                    observedAt = entry.observedAt, aboutTime = entry.aboutTime, useCurrentField = entry.readWorldField,
                    value = entry.referenceValue != null ? runtime.Reference(runtime.Entity(entry.referenceValue.instanceId)) : entry.value.Copy() });
            foreach (InitialReference reference in references)
            {
                string id = reference.target != null ? reference.target.instanceId : reference.informationId;
                if (!runtime.IsDiscovered(id)) throw new CommandException("치환 참조보다 먼저 대상을 발견해야 합니다: " + id);
                runtime.Store(reference.name, runtime.Reference(runtime.Entity(id) ?? throw new CommandException("치환 대상이 없습니다: " + id)));
            }
        }
    }
    [Serializable] public sealed class InitialDiscovery
    {
        public EntityInstanceAsset target;
        public string source = "초기 작전 자료";
        public bool observeFields, listEvidence = true;
    }
    [Serializable] public sealed class InformationEntry
    {
        public EntityInstanceAsset subject, sourceObject, referenceValue;
        public KeywordAsset keyword;
        public string field = "", source = "초기 작전 자료";
        public string sourceKey, statementKey;
        [TextArea] public string statement;
        public InformationKind kind;
        public GameDateTime observedAt, aboutTime;
        [Tooltip("Explicitly observe the world's current field. Leave off to author a claim that can differ from truth.")]
        public bool readWorldField;
        public Datum value = Datum.Empty;
    }
    [Serializable] public sealed class InferredInformation
    {
        public string id, name, type = "Concept", source = "Kernel 추론";
        [TextArea] public string content;
        public InformationKind kind = InformationKind.Inference;
    }
    [Serializable] public sealed class InitialReference
    {
        public string name;
        public EntityInstanceAsset target;
        [Tooltip("ID of an inference above, when Target is empty.")]
        public string informationId;
    }
}
