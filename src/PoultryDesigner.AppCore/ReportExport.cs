using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;

namespace PoultryDesigner.AppCore;

public static class ReportExport
{
    private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static void Excel(AllocationReport report, string path, bool overwrite = false)
    {
        Validate(report);
        Atomic(path, ".xlsx", overwrite, stream => {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, true);
            void Xml(string name, Action<XmlWriter> write) { using var part = zip.CreateEntry(name).Open(); using var w = XmlWriter.Create(part,
                new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false }); write(w); }
            void Attr(XmlWriter w, string name, object value) => w.WriteAttributeString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
            Xml("[Content_Types].xml", w => {
                w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                foreach (var d in new[] { ("rels", "application/vnd.openxmlformats-package.relationships+xml"), ("xml", "application/xml") }) {
                    w.WriteStartElement("Default"); Attr(w, "Extension", d.Item1); Attr(w, "ContentType", d.Item2); w.WriteEndElement(); }
                void Part(string name, string type) { w.WriteStartElement("Override"); Attr(w, "PartName", name); Attr(w, "ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml." + type + "+xml"); w.WriteEndElement(); }
                Part("/xl/workbook.xml", "sheet.main"); Part("/xl/styles.xml", "styles");
                for (int i = 0; i < report.Tables.Length; i++) Part($"/xl/worksheets/sheet{i + 1}.xml", "worksheet"); w.WriteEndElement(); });
            Xml("_rels/.rels", w => { w.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                w.WriteStartElement("Relationship"); Attr(w, "Id", "rId1"); Attr(w, "Type", RelNs + "/officeDocument"); Attr(w, "Target", "xl/workbook.xml"); w.WriteEndElement(); w.WriteEndElement(); });
            Xml("xl/workbook.xml", w => {
                w.WriteStartElement("workbook", SheetNs); w.WriteAttributeString("xmlns", "r", null, RelNs);
                w.WriteStartElement("bookViews"); w.WriteStartElement("workbookView"); Attr(w, "activeTab", 0); w.WriteEndElement(); w.WriteEndElement();
                w.WriteStartElement("sheets"); for (int i = 0; i < report.Tables.Length; i++) { w.WriteStartElement("sheet"); Attr(w, "name", report.Tables[i].Name); Attr(w, "sheetId", i + 1);
                    w.WriteAttributeString("r", "id", RelNs, $"rId{i + 1}"); w.WriteEndElement(); } w.WriteEndElement();
                w.WriteStartElement("definedNames"); for (int i = 0; i < report.Tables.Length; i++) { w.WriteStartElement("definedName"); Attr(w, "name", "_xlnm.Print_Titles"); Attr(w, "localSheetId", i);
                    w.WriteString($"'{report.Tables[i].Name.Replace("'", "''")}'!$1:$1"); w.WriteEndElement(); } w.WriteEndElement(); w.WriteEndElement(); });
            Xml("xl/_rels/workbook.xml.rels", w => { w.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                for (int i = 0; i <= report.Tables.Length; i++) { w.WriteStartElement("Relationship"); Attr(w, "Id", $"rId{i + 1}");
                    Attr(w, "Type", RelNs + (i == report.Tables.Length ? "/styles" : "/worksheet")); Attr(w, "Target", i == report.Tables.Length ? "styles.xml" : $"worksheets/sheet{i + 1}.xml"); w.WriteEndElement(); } w.WriteEndElement(); });
            Xml("xl/styles.xml", w => WriteStyles(w));
            for (int index = 0; index < report.Tables.Length; index++) {
                var table = report.Tables[index]; var side = table.SidePanel;
                var columns = side is null ? table.Columns : table.Columns.Add(new("", 3)).AddRange(side.Columns);
                int rowCount = Math.Max(table.Rows.Length, side?.Rows.Length ?? 0);
                Xml($"xl/worksheets/sheet{index + 1}.xml", w => {
                    w.WriteStartElement("worksheet", SheetNs);
                    w.WriteStartElement("dimension"); Attr(w, "ref", $"A1:{Column(columns.Length)}{rowCount + 1}"); w.WriteEndElement();
                    w.WriteStartElement("sheetViews"); w.WriteStartElement("sheetView"); Attr(w, "workbookViewId", 0); Attr(w, "showGridLines", 0);
                    w.WriteStartElement("pane"); Attr(w, "xSplit", 1); Attr(w, "ySplit", 1); Attr(w, "topLeftCell", "B2"); Attr(w, "activePane", "bottomRight"); Attr(w, "state", "frozen"); w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();
                    w.WriteStartElement("sheetFormatPr"); Attr(w, "defaultRowHeight", 24); w.WriteEndElement();
                    w.WriteStartElement("cols"); for (int i = 0; i < columns.Length; i++) { w.WriteStartElement("col"); Attr(w, "min", i + 1); Attr(w, "max", i + 1);
                        Attr(w, "width", Math.Max(columns[i].Width, Math.Min(42, columns[i].Name.Length + 3))); Attr(w, "customWidth", 1); w.WriteEndElement(); } w.WriteEndElement();
                    w.WriteStartElement("sheetData");
                    void Row(int row, IReadOnlyList<object?> values, bool header) {
                        w.WriteStartElement("row"); Attr(w, "r", row);
                        double lines = values.Select((v, i) => AllocationReports.Text(v).Split('\n').Sum(x => Math.Max(1, Math.Ceiling(x.Length / Math.Max(12, columns[i].Width - 2))))).DefaultIfEmpty(1).Max();
                        Attr(w, "ht", Math.Min(409, Math.Max(header ? 28 : 24, lines * 15 + 8))); Attr(w, "customHeight", 1);
                        for (int c = 0; c < values.Count; c++) {
                            object? value = values[c]; if (value is null) continue;
                            bool numeric = value is int or long or decimal or double; w.WriteStartElement("c"); Attr(w, "r", Column(c + 1) + row);
                            bool panelNumber = side is not null && c > table.Columns.Length && value is double;
                            int numberStyle = panelNumber ? value is double small && small > 0 && small < .000001 ? 5 : 4 : 2;
                            Attr(w, "s", header ? 1 : numeric ? value is int or long ? 3 : numberStyle : 0);
                            if (numeric) w.WriteElementString("v", AllocationReports.Text(value));
                            else { Attr(w, "t", "inlineStr"); w.WriteStartElement("is"); w.WriteStartElement("t"); w.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                                w.WriteString(AllocationReports.Text(value)); w.WriteEndElement(); w.WriteEndElement(); } w.WriteEndElement(); }
                        w.WriteEndElement(); }
                    Row(1, columns.Select(x => (object?)(x.Name.Length == 0 ? null : x.Name)).ToArray(), true);
                    for (int r = 0; r < rowCount; r++) {
                        var values = r < table.Rows.Length ? table.Rows[r] : Enumerable.Repeat<object?>(null, table.Columns.Length).ToImmutableArray();
                        if (side is not null) values = values.Add(null).AddRange(r < side.Rows.Length ? side.Rows[r] : Enumerable.Repeat<object?>(null, side.Columns.Length));
                        Row(r + 2, values, false);
                    }
                    w.WriteEndElement();
                    w.WriteStartElement("autoFilter"); Attr(w, "ref", $"A1:{Column(table.Columns.Length)}{table.Rows.Length + 1}"); w.WriteEndElement();
                    w.WriteStartElement("pageMargins"); foreach (string side in new[] { "left", "right", "top", "bottom" }) Attr(w, side, 0.4); Attr(w, "header", 0.2); Attr(w, "footer", 0.2); w.WriteEndElement();
                    w.WriteStartElement("pageSetup"); Attr(w, "paperSize", 1); Attr(w, "orientation", "landscape"); w.WriteEndElement();
                    w.WriteStartElement("headerFooter"); w.WriteElementString("oddHeader", "&L" + report.Title.Replace("&", "&&") + "&R" + table.Name);
                    w.WriteElementString("oddFooter", "&LChosen allocation snapshot&RPage &P of &N"); w.WriteEndElement(); w.WriteEndElement(); });
            }
        });
    }
    private static void WriteStyles(XmlWriter w)
    {
        w.WriteStartElement("styleSheet", SheetNs);
        w.WriteStartElement("numFmts"); w.WriteAttributeString("count", "3");
        foreach (var (id, format) in new[] { (164, "0.000"), (165, "0.000000"), (166, "0.000000E+00") }) {
            w.WriteStartElement("numFmt"); w.WriteAttributeString("numFmtId", id.ToString()); w.WriteAttributeString("formatCode", format); w.WriteEndElement();
        } w.WriteEndElement();
        w.WriteStartElement("fonts"); w.WriteAttributeString("count", "2");
        for (int i = 0; i < 2; i++) { w.WriteStartElement("font"); if (i == 1) w.WriteElementString("b", "");
            w.WriteStartElement("sz"); w.WriteAttributeString("val", "10"); w.WriteEndElement(); w.WriteStartElement("color"); w.WriteAttributeString("rgb", i == 1 ? "FFFFFFFF" : "FF17191B"); w.WriteEndElement();
            w.WriteStartElement("name"); w.WriteAttributeString("val", "Manrope"); w.WriteEndElement(); w.WriteEndElement(); } w.WriteEndElement();
        w.WriteStartElement("fills"); w.WriteAttributeString("count", "3");
        foreach (string pattern in new[] { "none", "gray125", "solid" }) { w.WriteStartElement("fill"); w.WriteStartElement("patternFill"); w.WriteAttributeString("patternType", pattern);
            if (pattern == "solid") { w.WriteStartElement("fgColor"); w.WriteAttributeString("rgb", "FF202326"); w.WriteEndElement(); w.WriteStartElement("bgColor"); w.WriteAttributeString("indexed", "64"); w.WriteEndElement(); }
            w.WriteEndElement(); w.WriteEndElement(); } w.WriteEndElement();
        w.WriteStartElement("borders"); w.WriteAttributeString("count", "1"); w.WriteStartElement("border"); foreach (string x in new[] { "left", "right", "top", "bottom", "diagonal" }) w.WriteElementString(x, ""); w.WriteEndElement(); w.WriteEndElement();
        w.WriteStartElement("cellStyleXfs"); w.WriteAttributeString("count", "1"); w.WriteStartElement("xf"); w.WriteAttributeString("numFmtId", "0"); w.WriteAttributeString("fontId", "0"); w.WriteAttributeString("fillId", "0"); w.WriteAttributeString("borderId", "0"); w.WriteEndElement(); w.WriteEndElement();
        w.WriteStartElement("cellXfs"); w.WriteAttributeString("count", "6");
        for (int i = 0; i < 6; i++) { w.WriteStartElement("xf"); w.WriteAttributeString("numFmtId", i == 2 ? "164" : i == 4 ? "165" : i == 5 ? "166" : "0"); w.WriteAttributeString("fontId", i == 1 ? "1" : "0");
            w.WriteAttributeString("fillId", i == 1 ? "2" : "0"); w.WriteAttributeString("borderId", "0"); w.WriteAttributeString("xfId", "0"); w.WriteAttributeString("applyAlignment", "1");
            if (i is 2 or 4 or 5) w.WriteAttributeString("applyNumberFormat", "1");
            w.WriteStartElement("alignment"); w.WriteAttributeString("horizontal", i == 1 ? "center" : i > 1 ? "right" : "left"); w.WriteAttributeString("vertical", "top"); w.WriteAttributeString("wrapText", "1"); w.WriteEndElement(); w.WriteEndElement(); }
        w.WriteEndElement(); w.WriteStartElement("cellStyles"); w.WriteAttributeString("count", "1"); w.WriteStartElement("cellStyle"); w.WriteAttributeString("name", "Normal"); w.WriteAttributeString("xfId", "0"); w.WriteAttributeString("builtinId", "0"); w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();
    }
    public static void Html(AllocationReport report, string path, bool overwrite = false, string? fontDirectory = null)
    {
        Validate(report);
        Atomic(path, ".html", overwrite, stream => {
            using var w = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            string H(string text) => WebUtility.HtmlEncode(text);
            w.Write("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + H(report.Title) + "</title><style>");
            if (fontDirectory is not null) foreach (var font in new[] { ("Manrope", "Manrope-Regular.otf", 400), ("Manrope", "Manrope-Bold.otf", 700), ("Poppins", "Poppins-Regular.otf", 400) })
                // The caller supplies the installed asset directory; filenames are literals, never workbook or report data.
                // nosemgrep: csharp.lang.security.filesystem.unsafe-path-combine.unsafe-path-combine
                w.Write($"@font-face{{font-family:'{font.Item1}';font-weight:{font.Item3};src:url(data:font/otf;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(fontDirectory, font.Item2)))}) format('opentype')}}");
            w.Write("body{font:11pt Manrope,'Segoe UI',sans-serif;color:#17191b;margin:32px}h1,h2{font-family:Poppins,Manrope,sans-serif}h1{font-size:20pt}h2{font-size:15pt}p{line-height:1.5}table{border-collapse:collapse;width:100%;table-layout:fixed;font-size:10pt}th{text-align:left;background:#202326;color:white}td,th{padding:8px;vertical-align:top;overflow-wrap:anywhere;white-space:pre-wrap;border-bottom:1px solid #d8dcde}tr:nth-child(even){background:#f3f5f5}section{margin-top:30px}thead{display:table-header-group}.context{border-left:4px solid #429e84;padding-left:12px}.tools{background:#17191b;color:white;padding:14px}.tools button{font:inherit;padding:8px 16px;background:#429e84;color:white;border:0;cursor:pointer}@page{size:letter landscape;margin:12mm}@media print{body{margin:0;font-size:10pt}.tools{display:none}section{break-before:page}section:first-of-type{break-before:auto}h2{break-after:avoid}tr{break-inside:avoid}th{print-color-adjust:exact;-webkit-print-color-adjust:exact}} </style><body>");
            w.Write("<div class=\"tools\"><button type=\"button\" onclick=\"window.print()\">Print / Save as PDF</button> <span>Landscape report. All rows are included.</span></div><h1>" + H(report.Title) + "</h1><p class=\"context\">" + H(report.Context) + "</p>");
            foreach (var table in AllocationReports.Printable(report).Tables) {
                w.Write("<section><h2>" + H(table.Name) + "</h2><p>" + H(table.Description) + "</p>");
                if (table.Rows.IsEmpty) { w.Write("<p>No records for this revision.</p></section>"); continue; }
                w.Write("<table><thead><tr>"); foreach (var c in table.Columns) w.Write("<th scope=\"col\">" + H(c.Name) + "</th>"); w.Write("</tr></thead><tbody>");
                foreach (var row in table.Rows) { w.Write("<tr>"); foreach (var cell in row) w.Write("<td>" + H(AllocationReports.PrintedText(cell)) + "</td>"); w.Write("</tr>"); }
                w.Write("</tbody></table></section>");
            }
            if (fontDirectory is not null) {
                w.Write("<template id=\"bundled-font-licenses\"><pre>");
                // The same trusted installed asset directory and fixed license basenames are used here.
                // nosemgrep: csharp.lang.security.filesystem.unsafe-path-combine.unsafe-path-combine
                foreach (string license in new[] { "Manrope-OFL.txt", "Poppins-OFL.txt" }) w.Write(H(File.ReadAllText(Path.Combine(fontDirectory, license))) + "\n");
                w.Write("</pre></template>");
            }
            w.Write("</body></html>");
        });
    }
    private static string Column(int index) { string value = ""; while (index > 0) { index--; value = (char)('A' + index % 26) + value; index /= 26; } return value; }
    private static void Validate(AllocationReport report)
    {
        XmlConvert.VerifyXmlChars(report.Title); XmlConvert.VerifyXmlChars(report.Context);
        if (report.Tables.IsDefaultOrEmpty || report.Tables.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != report.Tables.Length) throw new ArgumentException("Report tables must have unique names.");
        foreach (var table in report.Tables) {
            if (table.Name.Length is 0 or > 31 || table.Name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) >= 0 || table.Columns.Length is 0 or > 16384 || table.Rows.Length > 1048575) throw new ArgumentException("Report exceeds Excel sheet limits.");
            foreach (var c in table.Columns) XmlConvert.VerifyXmlChars(c.Name);
            foreach (var row in table.Rows) { if (row.Length != table.Columns.Length) throw new ArgumentException("Report row width is inconsistent.");
                foreach (var value in row) { string text = AllocationReports.Text(value); if (text.Length > 32767) throw new ArgumentException("A report cell exceeds Excel's 32,767-character limit. No data was truncated.");
                    XmlConvert.VerifyXmlChars(text); if (value is double d && !double.IsFinite(d)) throw new ArgumentException("Nonfinite report value."); } }
            if (table.SidePanel is { } side) {
                if (side.SidePanel is not null) throw new ArgumentException("Nested report side panels are not supported.");
                Validate(report with { Tables = [side] });
            }
        }
    }
    private static void Atomic(string path, string extension, bool overwrite, Action<Stream> write)
    {
        string target = Path.GetFullPath(path); if (!string.Equals(Path.GetExtension(target), extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use the " + extension + " file extension.");
        if (!overwrite && File.Exists(target)) throw new IOException("The export already exists.");
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".report-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { write(stream); stream.Flush(true); }
            File.Move(temporary, target, overwrite); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
