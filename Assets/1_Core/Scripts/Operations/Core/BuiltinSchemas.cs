using System;

namespace Codetective.Operations
{
    public static class BuiltinSchemas
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
                Names = Ko("설명보기"), Invoke = c => Datum.Text(c.Target.Label + " — " + c.Target.GetValue("description").text) });
            registry.Register(root);
            foreach (DataKind kind in Enum.GetValues(typeof(DataKind)))
            {
                if (kind == DataKind.Empty || kind == DataKind.Any || kind == DataKind.Text || kind == DataKind.Number ||
                    kind == DataKind.Boolean || kind == DataKind.Entity || kind == DataKind.Protocol || kind == DataKind.Task || kind == DataKind.Thread) continue;
                var schema = new EntitySchema(kind.ToString(), CommandVocabulary.KindName(kind, CommandLocale.Korean), kind, root)
                { Names = Ko(CommandVocabulary.KindName(kind, CommandLocale.Korean)) };
                registry.Register(schema);
            }
            registry.Register(new EntitySchema("Thread", "Thread", DataKind.Thread, registry.Require("Person")) { Names = Ko("스레드") });
            return registry;
        }
        internal static LocalizedCommandName[] Ko(string name) => new[] { new LocalizedCommandName(CommandLocale.Korean, name) };
    }
}
