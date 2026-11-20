using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Codetective.DataExchange
{
    public sealed class WorkbookSheet
    {
        public string Name;
        public readonly List<string[]> Rows = new List<string[]>();
        public readonly HashSet<int> NumericColumns = new HashSet<int>();
        public WorkbookSheet(string name) { Name = name; }
        public void Add(params string[] values) => Rows.Add(values);
    }
    /// <summary>Small, dependency-free OOXML workbooks. Formula strings are exported as literal text.</summary>
    public sealed class XlsxWorkbook
    {
        private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace C = "http://schemas.openxmlformats.org/package/2006/content-types";
        public readonly List<WorkbookSheet> Sheets = new List<WorkbookSheet>();
        public WorkbookSheet Add(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 31 || name.IndexOfAny(new[] { '[', ']', ':', '*', '?', '/', '\\' }) >= 0 || Sheets.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("워크시트 이름이 유효하지 않거나 중복됩니다: " + name);
            var sheet = new WorkbookSheet(name); Sheets.Add(sheet); return sheet;
        }
        public WorkbookSheet Require(string name) => Sheets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("워크시트가 없습니다: " + name);
        public void Write(Stream output)
        {
            if (Sheets.Count == 0) throw new InvalidDataException("내보낼 워크시트가 없습니다.");
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                var types = new XElement(C + "Types", new XElement(C + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(C + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(C + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")));
                var workbook = new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(S + "sheets"));
                var relationships = new XElement(P + "Relationships");
                for (int i = 0; i < Sheets.Count; i++)
                {
                    WorkbookSheet sheet = Sheets[i]; string id = "rId" + (i + 1), path = "worksheets/sheet" + (i + 1) + ".xml";
                    workbook.Element(S + "sheets").Add(new XElement(S + "sheet", new XAttribute("name", sheet.Name), new XAttribute("sheetId", i + 1), new XAttribute(R + "id", id)));
                    relationships.Add(new XElement(P + "Relationship", new XAttribute("Id", id), new XAttribute("Type", R.NamespaceName + "/worksheet"), new XAttribute("Target", path)));
                    types.Add(new XElement(C + "Override", new XAttribute("PartName", "/xl/" + path), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
                    var rows = new XElement(S + "sheetData");
                    int width = sheet.Rows.Count == 0 ? 1 : Math.Max(1, sheet.Rows.Max(row => row.Length));
                    for (int y = 0; y < sheet.Rows.Count; y++)
                    {
                        var row = new XElement(S + "row", new XAttribute("r", y + 1));
                        for (int x = 0; x < sheet.Rows[y].Length; x++)
                        {
                            string value = sheet.Rows[y][x] ?? "";
                            if (value.Length > 32767) throw new InvalidDataException("Excel 셀의 글자 수(32767)를 초과했습니다: " + sheet.Name + " " + Column(x) + (y + 1));
                            var cell = new XElement(S + "c", new XAttribute("r", Column(x) + (y + 1)));
                            if (y > 0 && sheet.NumericColumns.Contains(x) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !double.IsNaN(number) && !double.IsInfinity(number))
                                cell.Add(new XElement(S + "v", value));
                            else cell.Add(new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value)));
                            row.Add(cell);
                        }
                        rows.Add(row);
                    }
                    var xml = new XElement(S + "worksheet", new XElement(S + "dimension", new XAttribute("ref", "A1:" + Column(width - 1) + Math.Max(1, sheet.Rows.Count))),
                        new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0), new XElement(S + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                        new XElement(S + "cols", new XElement(S + "col", new XAttribute("min", 1), new XAttribute("max", width), new XAttribute("width", 26), new XAttribute("customWidth", 1))), rows);
                    if (sheet.Rows.Count > 1) xml.Add(new XElement(S + "autoFilter", new XAttribute("ref", "A1:" + Column(width - 1) + sheet.Rows.Count)));
                    Put(zip, "xl/" + path, xml);
                }
                Put(zip, "[Content_Types].xml", types); Put(zip, "xl/workbook.xml", workbook); Put(zip, "xl/_rels/workbook.xml.rels", relationships);
                Put(zip, "_rels/.rels", new XElement(P + "Relationships", new XElement(P + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", R.NamespaceName + "/officeDocument"), new XAttribute("Target", "xl/workbook.xml"))));
            }
        }
        public static XlsxWorkbook Read(Stream input)
        {
            var result = new XlsxWorkbook();
            using (var zip = new ZipArchive(input, ZipArchiveMode.Read, true))
            {
                if (zip.Entries.Count > 4096 || zip.Entries.Sum(e => e.Length) > 128L * 1024 * 1024) throw new InvalidDataException("워크북의 크기 또는 항목 수가 너무 큽니다.");
                XElement main = Get(zip, "xl/workbook.xml"), rels = Get(zip, "xl/_rels/workbook.xml.rels");
                XElement shared = Get(zip, "xl/sharedStrings.xml", false);
                string[] strings = shared == null ? Array.Empty<string>() : shared.Elements(S + "si").Select(Text).ToArray();
                foreach (XElement node in main.Element(S + "sheets")?.Elements(S + "sheet") ?? Enumerable.Empty<XElement>())
                {
                    string id = (string)node.Attribute(R + "id");
                    XElement rel = rels.Elements(P + "Relationship").SingleOrDefault(e => (string)e.Attribute("Id") == id);
                    if (rel == null || (string)rel.Attribute("TargetMode") == "External") throw new InvalidDataException("워크시트 연결이 유효하지 않습니다.");
                    var uri = new Uri(new Uri("https://workbook.invalid/xl/"), (string)rel.Attribute("Target"));
                    if (uri.Host != "workbook.invalid" || !uri.AbsolutePath.StartsWith("/xl/", StringComparison.Ordinal)) throw new InvalidDataException("워크시트 경로가 유효하지 않습니다.");
                    XElement xml = Get(zip, uri.AbsolutePath.TrimStart('/')); WorkbookSheet sheet = result.Add((string)node.Attribute("name"));
                    foreach (XElement row in xml.Element(S + "sheetData")?.Elements(S + "row") ?? Enumerable.Empty<XElement>())
                    {
                        var values = new SortedDictionary<int, string>();
                        foreach (XElement cell in row.Elements(S + "c"))
                        {
                            string reference = (string)cell.Attribute("r"); int column = ParseColumn(reference);
                            if (values.ContainsKey(column)) throw new InvalidDataException("셀이 중복됩니다: " + reference);
                            string type = (string)cell.Attribute("t"), value = (string)cell.Element(S + "v");
                            if (cell.Element(S + "f") != null && value == null) throw new InvalidDataException("수식 결과가 저장되지 않았습니다. Excel에서 계산 후 저장하세요: " + reference);
                            if (type == "e") throw new InvalidDataException("Excel 오류 셀이 있습니다: " + reference + " / " + value);
                            if (type == "inlineStr") value = Text(cell.Element(S + "is"));
                            if (type == "s")
                            { if (!int.TryParse(value, out int index) || index < 0 || index >= strings.Length) throw new InvalidDataException("공유 문자열 연결이 잘못되었습니다."); value = strings[index]; }
                            values[column] = value ?? "";
                        }
                        string[] data = new string[values.Count == 0 ? 0 : values.Keys.Max() + 1];
                        foreach (var pair in values) data[pair.Key] = pair.Value;
                        sheet.Rows.Add(data);
                    }
                }
            }
            return result;
        }
        private static string Text(XElement element) => element == null ? "" : string.Concat(element.Descendants(S + "t").Where(t => !t.Ancestors(S + "rPh").Any()).Select(t => t.Value));
        private static string Column(int value)
        { string result = ""; for (int n = value + 1; n > 0; n = (n - 1) / 26) result = (char)('A' + (n - 1) % 26) + result; return result; }
        private static int ParseColumn(string reference)
        {
            int result = 0, letters = 0;
            foreach (char c in reference ?? "") { if (c < 'A' || c > 'Z') break; result = checked(result * 26 + c - 'A' + 1); letters++; }
            if (letters == 0 || result > 16384) throw new InvalidDataException("셀 주소가 유효하지 않습니다: " + reference);
            return result - 1;
        }
        private static void Put(ZipArchive zip, string path, XElement xml)
        {
            using (Stream stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open())
            using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) })) new XDocument(xml).Save(writer);
        }
        private static XElement Get(ZipArchive zip, string path, bool required = true)
        {
            ZipArchiveEntry entry = zip.GetEntry(path);
            if (entry == null) { if (!required) return null; throw new InvalidDataException("XLSX 항목이 없습니다: " + path); }
            if (entry.Length > 32 * 1024 * 1024) throw new InvalidDataException("워크시트 XML이 너무 큽니다.");
            using (Stream stream = entry.Open()) using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) return XElement.Load(reader);
        }
    }
}
