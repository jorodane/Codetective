using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    // Flat nodes avoid recursive Unity inline serialization and retain nested dictionaries/arrays in saves.
    [Serializable] public sealed class ValueNode
    {
        public DataKind kind; public string id = "", label = "", text = "", schemaId = ""; public double number;
        public int[] children = Array.Empty<int>();
        public ValueNode Copy() => new ValueNode { kind = kind, id = id, label = label, text = text, schemaId = schemaId, number = number, children = children.ToArray() };
    }
    [Serializable] public sealed class CollectionValue
    {
        public ValueNode[] nodes = Array.Empty<ValueNode>();
        public CollectionValue Copy() => new CollectionValue { nodes = nodes.Select(n => n.Copy()).ToArray() };
        public static Datum Create(DataKind kind, IEnumerable<Datum> values)
        {
            var nodes = new List<ValueNode> { new ValueNode { kind = kind } };
            var children = new List<int>();
            foreach (Datum value in values)
            {
                int offset = nodes.Count; children.Add(offset);
                if (value.kind == DataKind.Array || value.kind == DataKind.Dictionary)
                {
                    value.collection.Validate();
                    nodes.AddRange(value.collection.nodes.Select(n => { ValueNode copy = n.Copy(); copy.children = copy.children.Select(c => c + offset).ToArray(); return copy; }));
                }
                else nodes.Add(new ValueNode { kind = value.kind, id = value.id, label = value.label, text = value.text, schemaId = value.schemaId, number = value.number });
                if (nodes.Count > 4096) throw new CommandException("자료 모음은 4096개 노드 이하로 작성하세요.");
            }
            nodes[0].children = children.ToArray();
            var graph = new CollectionValue { nodes = nodes.ToArray() }; graph.Validate();
            return new Datum { kind = kind, collection = graph };
        }
        public void Validate()
        {
            if (nodes == null || nodes.Length == 0 || nodes.Length > 4096) throw new CommandException("자료 모음이 유효하지 않습니다.");
            var active = new HashSet<int>(); var visited = new HashSet<int>();
            Action<int, int> visit = null;
            visit = (index, depth) =>
            {
                if (index < 0 || index >= nodes.Length || depth > 32 || !active.Add(index)) throw new CommandException("자료 모음에 순환/잘못된 연결이 있습니다.");
                ValueNode node = nodes[index];
                if (node == null || !Enum.IsDefined(typeof(DataKind), node.kind) || node.children == null || node.kind == DataKind.Dictionary && node.children.Length % 2 != 0 ||
                    node.kind != DataKind.Array && node.kind != DataKind.Dictionary && node.children.Length != 0)
                    throw new CommandException("자료 모음의 항목이 유효하지 않습니다.");
                if (node.kind == DataKind.Dictionary)
                {
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < node.children.Length; i += 2)
                    {
                        int key = node.children[i];
                        if (key < 0 || key >= nodes.Length || nodes[key] == null || nodes[key].kind == DataKind.Array || nodes[key].kind == DataKind.Dictionary ||
                            !keys.Add(nodes[key].kind + ":" + nodes[key].id + ":" + nodes[key].text + ":" + nodes[key].number)) throw new CommandException("사전의 키가 중복되었거나 유효하지 않습니다.");
                    }
                }
                foreach (int child in node.children) if (!visited.Contains(child)) visit(child, depth + 1);
                active.Remove(index); visited.Add(index);
            };
            visit(0, 0);
        }
        public Datum[] Elements()
        {
            Validate();
            return nodes[0].children.Select(index => Read(index)).ToArray();
        }
        private Datum Read(int index)
        {
            ValueNode node = nodes[index];
            if (node.kind == DataKind.Array || node.kind == DataKind.Dictionary)
                return Create(node.kind, node.children.Select(Read));
            return new Datum { kind = node.kind, id = node.id, label = node.label, text = node.text, schemaId = node.schemaId, number = node.number };
        }
    }
    public static class DataTypes
    {
        public static bool IsArray(string type) => type != null && type.EndsWith("[]", StringComparison.Ordinal);
        public static string[] DictionaryTypes(string type)
        {
            if (type == null || !type.StartsWith("Dict<", StringComparison.Ordinal) || !type.EndsWith(">", StringComparison.Ordinal)) return null;
            int depth = 0;
            for (int i = 5; i < type.Length - 1; i++)
            {
                if (type[i] == '<') depth++;
                if (type[i] == '>') depth--;
                if (type[i] == ',' && depth == 0) return new[] { type.Substring(5, i - 5), type.Substring(i + 1, type.Length - i - 2) };
            }
            throw new CommandException("사전의 키/값 자료형을 확인하세요.");
        }
        public static Parameter Parameter(string name, string type) => new Parameter(name,
            IsArray(type) ? DataKind.Array : DictionaryTypes(type) != null ? DataKind.Dictionary : CommandVocabulary.TryKind(type, out DataKind kind) ? kind : DataKind.Any,
            IsArray(type) || DictionaryTypes(type) != null || !CommandVocabulary.TryKind(type, out _) ? type : "");
    }
}
