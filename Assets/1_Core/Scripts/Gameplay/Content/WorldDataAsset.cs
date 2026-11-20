using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;

namespace Codetective.Gameplay
{
    public abstract class WorldDataAsset : EntityInstanceAsset
    {
        protected WorldDataAsset() { instanceId = "data_" + Guid.NewGuid().ToString("N"); initiallyKnown = false; }
        protected abstract void Write(DataAssetBuilder builder);
        private DataAssetBuilder Build() { var builder = new DataAssetBuilder(); Write(builder); return builder; }
        public override IEnumerable<InitialFieldDefinition> Values => Build().Values.Concat(initialValues ?? Array.Empty<InitialFieldDefinition>());
        public override IEnumerable<EntityInstanceAsset> Dependencies => Build().References;
    }
    public sealed class DataAssetBuilder
    {
        public readonly List<InitialFieldDefinition> Values = new List<InitialFieldDefinition>();
        public readonly List<EntityInstanceAsset> References = new List<EntityInstanceAsset>();
        public void Set(string name, Datum value) => Values.Add(new InitialFieldDefinition { field = name, value = value });
        public Datum Ref(EntityInstanceAsset asset)
        {
            if (asset == null) return Datum.Empty;
            References.Add(asset);
            DataKind kind = Enum.TryParse(asset.BuiltinSchemaId, out DataKind builtin) ? builtin : asset.BuiltinSchemaId == "Tool" ? DataKind.Item : DataKind.Entity;
            return new Datum { kind = kind, id = asset.instanceId, label = string.IsNullOrWhiteSpace(asset.displayName) ? asset.name : asset.displayName, schemaId = asset.SchemaId };
        }
        public void Reference(string name, EntityInstanceAsset asset) => Set(name, Ref(asset));
        public void List(string name, IEnumerable<EntityInstanceAsset> assets)
        {
            EntityInstanceAsset[] list = (assets ?? Array.Empty<EntityInstanceAsset>()).ToArray();
            if (list.Any(a => a == null)) throw new CommandException("자료 배열에 빈 참조가 있습니다: " + name);
            Set(name, Datum.List(list.Select(Ref).ToArray()));
        }
        public void Relations(string name, RelationLink[] links)
        {
            var pairs = new List<Datum>();
            foreach (RelationLink link in links ?? Array.Empty<RelationLink>())
            {
                if (link == null || link.target == null || link.relations == null || link.relations.Any(r => r == null)) throw new CommandException("관계 연결의 대상/관계가 비어 있습니다.");
                pairs.Add(Ref(link.target)); pairs.Add(Datum.List(link.relations.Select(Ref).ToArray()));
            }
            Set(name, Datum.Dict(pairs.ToArray()));
        }
    }
    [Serializable] public sealed class RelationLink { public PhysicalAsset target; public RelationshipAsset[] relations = Array.Empty<RelationshipAsset>(); }
    [Serializable] public sealed class OrganizationMember { public PersonAsset person; public string rank; }
    [Serializable] public sealed class OrganizationPlace { public PlaceAsset place; public string purpose; }
}
