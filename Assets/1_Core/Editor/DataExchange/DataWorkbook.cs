using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Codetective.Gameplay;
using Codetective.Operations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Codetective.DataExchange
{
    public sealed class WorkbookLocation { public string Guid = "", Path = ""; public long FileId; }
    public interface IWorkbookAssets
    {
        WorkbookLocation Locate(Object asset);
        Object Resolve(string guid, long fileId);
    }
    public sealed class WorkbookAsset
    {
        public string Key, Type, Guid, Path, Name;
        public ScriptableObject Value;
    }
    public sealed class DataWorkbookPlan : IDisposable
    {
        public readonly List<WorkbookAsset> Assets = new List<WorkbookAsset>();
        public void Dispose() { foreach (WorkbookAsset asset in Assets) if (asset.Value != null) Object.DestroyImmediate(asset.Value); }
        public void Validate()
        {
            WorldDefinition[] worlds = Assets.Select(a => a.Value).OfType<WorldDefinition>().ToArray();
            var covered = new HashSet<ScriptableObject>();
            foreach (WorldDefinition world in worlds)
            { world.CreateRuntime(); covered.UnionWith(DataWorkbook.CollectAssets(world)); }
            ScriptableObject[] remaining = Assets.Select(a => a.Value).Where(a => !covered.Contains(a)).ToArray();
            var runtime = ClinicScenario.Create();
            ContentTextTableAsset.Install(runtime, remaining.OfType<ContentTextTableAsset>().ToArray());
            EntityAssetCompiler.Install(runtime, remaining.OfType<EntitySchemaAsset>().ToArray(), remaining.OfType<EntityInstanceAsset>().ToArray());
            foreach (CurrentInformationAsset information in remaining.OfType<CurrentInformationAsset>()) information.Apply(runtime);
            foreach (ContentTextTableAsset table in Assets.Select(a => a.Value).OfType<ContentTextTableAsset>()) new ContentTextCatalog().Install(table.entries);
            foreach (DataViewerAsset viewer in Assets.Select(a => a.Value).OfType<DataViewerAsset>())
                if (!Enum.IsDefined(typeof(DataViewerKind), viewer.layout) || viewer.fields == null || viewer.fields.Any(string.IsNullOrWhiteSpace) || viewer.fields.Distinct().Count() != viewer.fields.Length)
                    throw new InvalidDataException("Viewer 레이아웃 또는 필드 목록이 유효하지 않습니다: " + viewer.name);
        }
    }
    /// <summary>Typed columns for ordinary fields; normalized paths for arrays, dictionaries and protocol definitions.</summary>
    public static class DataWorkbook
    {
        public static readonly IReadOnlyDictionary<string, Type> Types = new Dictionary<string, Type>(StringComparer.Ordinal) {
            { "World", typeof(WorldDefinition) }, { "CurrentInformation", typeof(CurrentInformationAsset) }, { "Viewer", typeof(DataViewerAsset) },
            { "TextTable", typeof(ContentTextTableAsset) },
            { "Schema", typeof(EntitySchemaAsset) }, { "Instance", typeof(EntityInstanceAsset) }, { "Concept", typeof(ConceptAsset) },
            { "Physical", typeof(PhysicalAsset) }, { "Keyword", typeof(KeywordAsset) }, { "Time", typeof(TimeAsset) }, { "Photo", typeof(PhotoAsset) },
            { "Relationship", typeof(RelationshipAsset) }, { "Statement", typeof(StatementAsset) }, { "Place", typeof(PlaceAsset) },
            { "Event", typeof(EventAsset) }, { "Item", typeof(ItemAsset) }, { "Tool", typeof(ToolAsset) }, { "Person", typeof(PersonAsset) }, { "Organization", typeof(OrganizationAsset) }
        };
        public static bool Supported(Object asset) => asset != null && Types.Values.Contains(asset.GetType());
        public static string TypeName(Type type) => Types.FirstOrDefault(p => p.Value == type).Key ?? throw new InvalidDataException("지원하지 않는 자료 에셋입니다: " + type.Name);
        private static string Identity(Type type) => typeof(EntityInstanceAsset).IsAssignableFrom(type) ? "instanceId" : type == typeof(EntitySchemaAsset) ? "schemaId" :
            type == typeof(WorldDefinition) ? "worldId" : type == typeof(CurrentInformationAsset) ? "informationId" : type == typeof(ContentTextTableAsset) ? "tableId" : "viewerId";
        public static IEnumerable<FieldInfo> Fields(Type type)
        {
            var chain = new Stack<Type>();
            for (Type item = type; item != null && item != typeof(ScriptableObject) && item != typeof(Object); item = item.BaseType) chain.Push(item);
            return chain.SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(f => !f.IsStatic && !f.IsDefined(typeof(NonSerializedAttribute), false) && (f.IsPublic || f.IsDefined(typeof(SerializeField), false)));
        }
        private static bool Number(Type type) => type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(decimal);
        private static bool Simple(Type type) => type == typeof(string) || type == typeof(bool) || type.IsEnum || Number(type) || typeof(Object).IsAssignableFrom(type);
        private static string Key(ScriptableObject asset)
        {
            string id = (string)asset.GetType().GetField(Identity(asset.GetType())).GetValue(asset);
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("고정 ID가 비어 있습니다: " + asset.name);
            return asset is EntityInstanceAsset ? id : TypeName(asset.GetType()).ToLowerInvariant() + ":" + id;
        }
        public static ScriptableObject[] CollectAssets(ScriptableObject root)
        {
            if (!Supported(root)) throw new InvalidDataException("월드 또는 자료 에셋을 선택하세요.");
            var assets = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
            var active = new HashSet<object>();
            void Collect(object value, int depth)
            {
                if (value == null) return;
                if (depth > 64) throw new InvalidDataException("중첩 자료가 너무 깊습니다.");
                if (value is Object unity)
                {
                    if (!Supported(unity)) return;
                    var asset = (ScriptableObject)unity; string key = Key(asset);
                    if (assets.TryGetValue(key, out ScriptableObject duplicate)) { if (duplicate != asset) throw new InvalidDataException("고정 ID가 중복됩니다: " + key); return; }
                    assets.Add(key, asset);
                    foreach (FieldInfo field in Fields(asset.GetType())) Collect(field.GetValue(asset), depth + 1);
                }
                else if (!Simple(value.GetType()))
                {
                    if (!active.Add(value)) throw new InvalidDataException("소유 데이터에 순환 참조가 있습니다. 대상 연결은 에셋 참조를 사용하세요.");
                    if (value is Array list) foreach (object item in list) Collect(item, depth + 1);
                    else foreach (FieldInfo field in Fields(value.GetType())) Collect(field.GetValue(value), depth + 1);
                    active.Remove(value);
                }
            }
            Collect(root, 0);
            return assets.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToArray();
        }
        public static XlsxWorkbook Export(ScriptableObject root, IWorkbookAssets resources)
        {
            var assets = CollectAssets(root).ToDictionary(Key, a => a, StringComparer.Ordinal);
            var book = new XlsxWorkbook();
            WorkbookSheet readme = book.Add("Readme"); readme.Add("Codetective Data Workbook", "1");
            readme.Add("루트", Key(root));
            readme.Add("편집", "자료형별 시트에서 이름·성별·나이·설명 등을 수정합니다. ~null은 비어 있는 값이며, 문자 그대로 ~로 시작하는 내용은 ~를 하나 더 붙입니다.");
            readme.Add("연결", "@대상ID는 이 파일의 에셋 참조입니다. asset:GUID:FileID는 이 Unity 프로젝트의 사진·프리팹 등 외부 에셋입니다.");
            readme.Add("번역", "displayNameKey와 Text Keys는 Translations 시트의 key를 참조합니다. Display Name은 원문/미번역 시 기본값입니다. 명령 식별자·치환 이름은 번역하지 않습니다.");
            readme.Add("배열 / 함수", "Details의 Path를 사용합니다. array 행의 Value는 배열 길이이며, 항목은 [0], [1] 순서입니다. 추가·삭제 시 길이도 수정하세요.");
            readme.Add("가져오기", "전체를 검증한 뒤 기존 GUID를 유지해 갱신합니다. 원본 에셋 파일은 삭제하지 않습니다. ID는 저장·명령 참조에 사용하므로 배포한 콘텐츠의 ID는 유지하세요.");
            WorkbookSheet manifest = book.Add("Assets"); manifest.Add("Key", "Type", "Id", "Guid", "Path", "Name");
            WorkbookSheet details = book.Add("Details"); details.Add("Asset", "Path", "Kind", "Value");
            WorkbookSheet translations = book.Add("Translations"); translations.Add("Asset", "key", "language", "text");
            foreach (var group in assets.OrderBy(p => p.Key, StringComparer.Ordinal).GroupBy(p => TypeName(p.Value.GetType())))
            {
                Type type = Types[group.Key]; FieldInfo[] fields = Fields(type).Where(f => f.Name != Identity(type)).ToArray();
                FieldInfo[] columns = fields.Where(f => Simple(f.FieldType)).ToArray();
                WorkbookSheet sheet = book.Add(group.Key); sheet.Add(new[] { "Asset" }.Concat(columns.Select(f => f.Name)).ToArray());
                for (int i = 0; i < columns.Length; i++) if (Number(columns[i].FieldType)) sheet.NumericColumns.Add(i + 1);
                foreach (var pair in group)
                {
                    ScriptableObject asset = pair.Value; WorkbookLocation where = resources.Locate(asset) ?? new WorkbookLocation();
                    manifest.Add(pair.Key, group.Key, (string)type.GetField(Identity(type)).GetValue(asset), where.Guid, where.Path, asset.name);
                    sheet.Add(new[] { pair.Key }.Concat(columns.Select(f => Encode(f.GetValue(asset), resources))).ToArray());
                    foreach (FieldInfo field in fields.Where(f => !Simple(f.FieldType) && !(asset is ContentTextTableAsset && f.Name == "entries"))) Flatten(details, pair.Key, field.Name, field.GetValue(asset), resources, 0);
                    if (asset is ContentTextTableAsset table)
                        foreach (TextTranslation row in table.entries)
                            translations.Add(pair.Key, Encode(row.key, resources), Encode(row.language, resources), Encode(row.text, resources));
                }
            }
            return book;
        }
        private static string Encode(object value, IWorkbookAssets resources)
        {
            if (value == null || value is Object empty && empty == null) return "~null";
            if (value is Object unity)
            {
                if (Supported(unity)) return "@" + Key((ScriptableObject)unity);
                WorkbookLocation where = resources.Locate(unity);
                if (where == null || string.IsNullOrEmpty(where.Guid)) throw new InvalidDataException("프로젝트에 저장되지 않은 에셋입니다: " + unity.name);
                return "asset:" + where.Guid + ":" + where.FileId.ToString(CultureInfo.InvariantCulture);
            }
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return value is string && text.StartsWith("~", StringComparison.Ordinal) ? "~" + text : text;
        }
        private static void Flatten(WorkbookSheet sheet, string owner, string path, object value, IWorkbookAssets resources, int depth)
        {
            if (depth > 64) throw new InvalidDataException("자료 경로가 너무 깊습니다: " + path);
            if (value == null) { sheet.Add(owner, path, "null", ""); return; }
            Type type = value.GetType();
            if (Simple(type)) { sheet.Add(owner, path, "value", Encode(value, resources)); return; }
            if (value is Array array)
            {
                sheet.Add(owner, path, "array", array.Length.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < array.Length; i++) Flatten(sheet, owner, path + "[" + i + "]", array.GetValue(i), resources, depth + 1);
                return;
            }
            sheet.Add(owner, path, "object", "");
            foreach (FieldInfo field in Fields(type)) Flatten(sheet, owner, path + "." + field.Name, field.GetValue(value), resources, depth + 1);
        }
        public static DataWorkbookPlan Prepare(XlsxWorkbook book, IWorkbookAssets resources, Func<Type, string, ScriptableObject> create)
        {
            var plan = new DataWorkbookPlan();
            try
            {
                var map = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
                foreach (Dictionary<string, string> row in Table(book.Require("Assets")))
                {
                    string key = Get(row, "Key"), typeName = Get(row, "Type");
                    if (!Types.TryGetValue(typeName, out Type type)) throw new InvalidDataException("지원하지 않는 자료형입니다: " + typeName);
                    ScriptableObject asset = create(type, Get(row, "Guid"));
                    if (asset == null || asset.GetType() != type) throw new InvalidDataException("기존 에셋과 자료형이 다릅니다: " + key);
                    if (map.ContainsKey(key)) { Object.DestroyImmediate(asset); throw new InvalidDataException("에셋 키가 중복됩니다: " + key); }
                    map.Add(key, asset); asset.name = Get(row, "Name");
                    plan.Assets.Add(new WorkbookAsset { Key = key, Type = typeName, Guid = Get(row, "Guid"), Path = Get(row, "Path"), Name = asset.name, Value = asset });
                    type.GetField(Identity(type)).SetValue(asset, Get(row, "Id"));
                }
                var tables = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.Ordinal);
                foreach (string typeName in plan.Assets.Select(a => a.Type).Distinct())
                {
                    var table = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                    foreach (var row in Table(book.Require(typeName)))
                    {
                        string key = Get(row, "Asset");
                        if (!map.ContainsKey(key) || TypeName(map[key].GetType()) != typeName) throw new InvalidDataException("자료형 시트의 에셋이 없거나 자료형이 다릅니다: " + key);
                        if (table.ContainsKey(key)) throw new InvalidDataException("자료형 시트의 에셋 행이 중복됩니다: " + key);
                        table.Add(key, row);
                    }
                    tables.Add(typeName, table);
                }
                foreach (WorkbookAsset item in plan.Assets)
                {
                    if (!tables[item.Type].TryGetValue(item.Key, out var row)) throw new InvalidDataException("자료형 시트에 에셋 행이 없습니다: " + item.Key);
                    foreach (var cell in row.Where(p => p.Key != "Asset"))
                    {
                        FieldInfo field = Field(item.Value.GetType(), cell.Key);
                        if (!Simple(field.FieldType) || field.Name == Identity(item.Value.GetType())) throw new InvalidDataException("일반 열에 사용할 수 없는 필드입니다: " + field.Name);
                        field.SetValue(item.Value, Decode(cell.Value, field.FieldType, map, resources));
                    }
                }
                Dictionary<string, string>[] detailRows = Table(book.Require("Details")).ToArray();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in detailRows.OrderBy(r => Get(r, "Path").Count(c => c == '.' || c == '[')))
                {
                    string owner = Get(row, "Asset"), path = Get(row, "Path");
                    if (!map.TryGetValue(owner, out ScriptableObject asset)) throw new InvalidDataException("Details의 에셋이 없습니다: " + owner);
                    string top = path.Split('.', '[')[0];
                    if (asset is ContentTextTableAsset && top == "entries") throw new InvalidDataException("번역 문구는 Translations 시트에서 수정하세요.");
                    if (Simple(Field(asset.GetType(), top).FieldType)) throw new InvalidDataException("일반 필드는 자료형별 시트에서 수정하세요: " + path);
                    if (!seen.Add(owner + "\n" + path)) throw new InvalidDataException("자료 경로가 중복됩니다: " + owner + "." + path);
                    SetPath(asset, path, Get(row, "Kind"), Get(row, "Value"), map, resources);
                }
                var textTables = plan.Assets.Where(a => a.Value is ContentTextTableAsset).ToDictionary(a => a.Key, a => (ContentTextTableAsset)a.Value, StringComparer.Ordinal);
                if (textTables.Count > 0)
                {
                    var translated = textTables.Keys.ToDictionary(k => k, k => new List<TextTranslation>(), StringComparer.Ordinal);
                    foreach (var row in Table(book.Require("Translations")))
                    {
                        string owner = Get(row, "Asset");
                        if (!translated.TryGetValue(owner, out var entries)) throw new InvalidDataException("번역 시트의 Text Table 에셋이 없습니다: " + owner);
                        string String(string column) => (string)Decode(Get(row, column), typeof(string), map, resources);
                        entries.Add(new TextTranslation { key = String("key"), language = String("language"), text = String("text") });
                    }
                    foreach (var table in textTables) table.Value.entries = translated[table.Key].ToArray();
                }
                var byGuid = plan.Assets.Where(a => !string.IsNullOrEmpty(a.Guid)).ToDictionary(a => a.Guid, a => a.Value, StringComparer.Ordinal);
                foreach (WorkbookAsset item in plan.Assets) RewriteReferences(item.Value, obj =>
                {
                    if (obj == null || map.Values.Contains(obj)) return obj;
                    WorkbookLocation location = resources.Locate(obj);
                    return location != null && byGuid.TryGetValue(location.Guid, out ScriptableObject clone) ? clone : obj;
                });
                plan.Validate(); return plan;
            }
            catch { plan.Dispose(); throw; }
        }
        private static object Decode(string value, Type type, IReadOnlyDictionary<string, ScriptableObject> map, IWorkbookAssets resources)
        {
            if (value == "~null") { if (type.IsValueType) throw new InvalidDataException("값 형식에 null을 넣을 수 없습니다: " + type.Name); return null; }
            if (typeof(Object).IsAssignableFrom(type))
            {
                if (string.IsNullOrEmpty(value)) return null;
                Object result;
                if (value.StartsWith("@", StringComparison.Ordinal))
                { if (!map.TryGetValue(value.Substring(1), out ScriptableObject found)) throw new InvalidDataException("연결된 에셋이 없습니다: " + value); result = found; }
                else
                {
                    string[] parts = value.Split(':');
                    if (parts.Length != 3 || parts[0] != "asset" || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) throw new InvalidDataException("에셋 참조 형식이 잘못되었습니다: " + value);
                    result = resources.Resolve(parts[1], id) ?? throw new InvalidDataException("프로젝트에서 연결할 사진/에셋을 찾을 수 없습니다: " + value);
                }
                if (!type.IsInstanceOfType(result)) throw new InvalidDataException("에셋 연결의 자료형이 다릅니다: " + value + " → " + type.Name);
                return result;
            }
            if (type == typeof(string)) return value.StartsWith("~~", StringComparison.Ordinal) ? value.Substring(1) : value;
            if (type == typeof(bool)) return value == "1" ? true : value == "0" ? false : bool.Parse(value);
            if (type.IsEnum)
            { object parsed = Enum.Parse(type, value, true); if (!Enum.IsDefined(type, parsed)) throw new InvalidDataException("열거 값이 유효하지 않습니다: " + value); return parsed; }
            if (Number(type))
            {
                decimal number = decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (type != typeof(double) && type != typeof(float) && type != typeof(decimal) && decimal.Truncate(number) != number) throw new InvalidDataException("정수 필드에 소수가 있습니다: " + value);
                return Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
            }
            throw new InvalidDataException("지원하지 않는 값 형식입니다: " + type.Name);
        }
        private static FieldInfo Field(Type type, string name) => Fields(type).FirstOrDefault(f => f.Name == name) ?? throw new InvalidDataException("자료 필드가 없습니다: " + type.Name + "." + name);
        private static void SetPath(object root, string path, string kind, string text, IReadOnlyDictionary<string, ScriptableObject> map, IWorkbookAssets resources)
        {
            string[] parts = path.Split('.'); if (parts.Length > 64) throw new InvalidDataException("필드 경로가 너무 깁니다.");
            object parent = root;
            for (int p = 0; p < parts.Length; p++)
            {
                Match match = Regex.Match(parts[p], @"^([A-Za-z_][A-Za-z0-9_]*)(?:\[([0-9]+)\])?$");
                if (!match.Success) throw new InvalidDataException("필드 경로가 유효하지 않습니다: " + path);
                FieldInfo field = Field(parent.GetType(), match.Groups[1].Value); Type type = field.FieldType;
                object value = field.GetValue(parent); Array array = null; int index = -1;
                if (match.Groups[2].Success)
                {
                    if (!type.IsArray || !int.TryParse(match.Groups[2].Value, out index) || !(value is Array current) || index >= current.Length) throw new InvalidDataException("배열 길이와 인덱스가 다릅니다: " + path);
                    array = current; type = type.GetElementType(); value = array.GetValue(index);
                }
                if (p + 1 == parts.Length)
                {
                    object assigned;
                    if (kind == "null") { if (type.IsValueType) throw new InvalidDataException("null을 넣을 수 없습니다: " + path); assigned = null; }
                    else if (kind == "array")
                    {
                        if (!type.IsArray || !int.TryParse(text, out int length) || length < 0 || length > 100000) throw new InvalidDataException("배열 길이가 유효하지 않습니다: " + path);
                        Array replacement = Array.CreateInstance(type.GetElementType(), length);
                        if (value is Array old) Array.Copy(old, replacement, Math.Min(old.Length, length)); assigned = replacement;
                    }
                    else if (kind == "object")
                    { if (Simple(type) || type.IsArray || type.IsAbstract) throw new InvalidDataException("객체 경로가 유효하지 않습니다: " + path); assigned = value ?? Activator.CreateInstance(type); }
                    else if (kind == "value") assigned = Decode(text, type, map, resources);
                    else throw new InvalidDataException("Details 종류가 유효하지 않습니다: " + kind);
                    if (array == null) field.SetValue(parent, assigned); else array.SetValue(assigned, index);
                }
                else
                {
                    if (typeof(Object).IsAssignableFrom(type)) throw new InvalidDataException("연결된 에셋은 자료형별 시트에서 편집하세요: " + path);
                    parent = value ?? throw new InvalidDataException("부모 데이터가 비어 있습니다: " + path);
                }
            }
        }
        public static void RewriteReferences(object value, Func<Object, Object> replace)
        {
            if (value == null) return;
            foreach (FieldInfo field in Fields(value.GetType()))
            {
                object item = field.GetValue(value);
                if (item is Object reference) field.SetValue(value, replace(reference));
                else if (item is Array array)
                    for (int i = 0; i < array.Length; i++) { object entry = array.GetValue(i); if (entry is Object asset) array.SetValue(replace(asset), i); else if (entry != null && !Simple(entry.GetType())) RewriteReferences(entry, replace); }
                else if (item != null && !Simple(item.GetType())) RewriteReferences(item, replace);
            }
        }
        private static string Get(IReadOnlyDictionary<string, string> row, string key) => row.TryGetValue(key, out string value) ? value ?? "" : throw new InvalidDataException("필수 열이 없습니다: " + key);
        private static IEnumerable<Dictionary<string, string>> Table(WorkbookSheet sheet)
        {
            if (sheet.Rows.Count == 0) throw new InvalidDataException("열 제목이 없습니다: " + sheet.Name);
            string[] columns = sheet.Rows[0];
            if (columns.Any(string.IsNullOrWhiteSpace) || columns.Distinct(StringComparer.Ordinal).Count() != columns.Length) throw new InvalidDataException("열 제목이 비어 있거나 중복됩니다: " + sheet.Name);
            foreach (string[] row in sheet.Rows.Skip(1))
            {
                if (row.All(string.IsNullOrEmpty)) continue;
                if (row.Length > columns.Length && row.Skip(columns.Length).Any(v => !string.IsNullOrEmpty(v))) throw new InvalidDataException("제목 없는 열에 값이 있습니다: " + sheet.Name);
                yield return columns.Select((column, index) => new { column, value = index < row.Length ? row[index] ?? "" : "" }).ToDictionary(p => p.column, p => p.value, StringComparer.Ordinal);
            }
        }
    }
}
