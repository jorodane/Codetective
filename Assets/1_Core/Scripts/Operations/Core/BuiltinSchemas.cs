using System;

namespace Codetective.Operations
{
    public static partial class BuiltinSchemas
    {
        public static SchemaRegistry Create()
        {
            var registry = new SchemaRegistry();
            var root = new EntitySchema("Entity", "개체", DataKind.Entity)
            { Names = new[] { new LocalizedCommandName(CommandLocale.Korean, "개체") } };
            root.Variables.Add(new VariableDefinition { Code = "id", Label = "고정 식별자", Kind = DataKind.Text,
                Getter = e => Datum.Text(e.Id), Names = Ko("식별자") });
            root.Variables.Add(new VariableDefinition { Code = "name", Label = "이름", Kind = DataKind.Text,
                Getter = e => Datum.Text(e.Label), Names = Ko("이름") });
            root.Variables.Add(new VariableDefinition { Code = "description", Label = "상세 정보", Kind = DataKind.Text,
                DefaultValue = Datum.Text(""), Names = Ko("상세정보") });
            root.Operations.Add(new Operation { Code = "describe", Label = "개체 정보 확인", ReturnKind = DataKind.Text,
                Names = Ko("설명보기"), Invoke = c => Datum.Text(c.Target.Label + " — " + c.Runtime.LastKnownValue(c.Target.Id, "description").text) });
            registry.Register(root);
            var concept = new EntitySchema("Concept", "추상개념", DataKind.Entity, root) { Names = Ko("추상개념") };
            concept.Variables.Add(new VariableDefinition { Code = "features", Kind = DataKind.Array, SchemaId = "Text[]", DefaultValue = Datum.List(), Names = Ko("특징") });
            registry.Register(concept);
            var physical = new EntitySchema("Physical", "실물", DataKind.Entity, concept) { Names = Ko("실물") }; registry.Register(physical);
            var keyword = new EntitySchema("Keyword", "키워드", DataKind.Keyword, concept) { Names = Ko("키워드") }; registry.Register(keyword);
            foreach (DataKind kind in Enum.GetValues(typeof(DataKind)))
            {
                if (kind == DataKind.Empty || kind == DataKind.Any || kind == DataKind.Text || kind == DataKind.Number ||
                    kind == DataKind.Boolean || kind == DataKind.Entity || kind == DataKind.Protocol || kind == DataKind.Task || kind == DataKind.Thread ||
                    kind == DataKind.Array || kind == DataKind.Dictionary || kind == DataKind.Keyword) continue;
                var schema = new EntitySchema(kind.ToString(), CommandVocabulary.KindName(kind, CommandLocale.Korean), kind,
                    kind == DataKind.Clue ? keyword : kind == DataKind.Person || kind == DataKind.Item || kind == DataKind.Place || kind == DataKind.Organization ? physical : concept)
                { Names = Ko(CommandVocabulary.KindName(kind, CommandLocale.Korean)) };
                registry.Register(schema);
            }
            registry.Register(new EntitySchema("Thread", "Thread", DataKind.Thread, registry.Require("Person")) { Names = Ko("스레드") });
            registry.Register(new EntitySchema("Tool", "도구", DataKind.Item, registry.Require("Item")) { Names = Ko("도구") });
            PopulateCommon(registry);
            return registry;
        }
        internal static LocalizedCommandName[] Ko(string name) => new[] { new LocalizedCommandName(CommandLocale.Korean, name) };
    }
}
