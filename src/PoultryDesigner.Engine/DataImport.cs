using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PoultryDesigner.Engine;

public static class DataImport
{
    public const int MaximumSourceBytes = 32 * 1024 * 1024;
    private const long MaximumExpandedBytes = 128 * 1024 * 1024;
    private const int MaximumCells = 2_000_000;
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static ImportSource Read(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length > MaximumSourceBytes) throw new InvalidDataException("Source exceeds 32 MiB.");
        using var buffer = new MemoryStream(); input.CopyTo(buffer);
        return Read(buffer.ToArray(), Path.GetFileName(path), Path.GetFullPath(path));
    }

    public static ImportSource Read(byte[] content, string fileName, string originalPath = "")
    {
        if (content.Length > MaximumSourceBytes) throw new InvalidDataException("Source exceeds 32 MiB.");
        string format = Path.GetExtension(fileName).ToLowerInvariant();
        ImmutableArray<SourceTable> tables = format switch
        {
            ".csv" => [Delimited(content, ',')],
            ".tsv" => [Delimited(content, '\t')],
            ".xlsx" or ".xlsm" => Workbook(content),
            _ => throw new InvalidDataException("Supported imports: UTF-8 CSV/TSV and XLSX/XLSM. Convert XLS explicitly first.")
        };
        return new(Guid.NewGuid().ToString("N"), fileName, originalPath, Convert.ToHexString(SHA256.HashData(content)),
            Convert.ToBase64String(content), format, DateTimeOffset.UtcNow, tables);
    }

    private static SourceTable Delimited(byte[] content, char separator)
    {
        string text = new UTF8Encoding(false, true).GetString(content).TrimStart('\uFEFF');
        var rows = ImmutableArray.CreateBuilder<SourceRow>();
        var cells = ImmutableArray.CreateBuilder<SourceCell>();
        var field = new StringBuilder();
        bool quoted = false, closed = false, atStart = true;
        int line = 1, start = 1, total = 0;
        void Cell()
        {
            if (++total > MaximumCells || cells.Count >= 16_384) throw new InvalidDataException("Too many source cells/columns.");
            cells.Add(new(cells.Count + 1, field.ToString())); field.Clear(); closed = false; atStart = true;
        }
        void Row()
        {
            Cell(); rows.Add(new(start, cells.ToImmutable())); cells.Clear();
            if (rows.Count > AllocationEngine.MaximumAnimals + 10_000) throw new InvalidDataException("Too many source rows.");
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else { field.Append(c); if (c == '\n' || (c == '\r' && (i + 1 == text.Length || text[i + 1] != '\n'))) line++; }
                continue;
            }
            if (c == separator) { Cell(); continue; }
            if (c is '\r' or '\n')
            {
                Row(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                start = ++line; continue;
            }
            if (closed) throw new InvalidDataException($"Unexpected character after closing quote on line {line}.");
            if (c == '"')
            {
                if (!atStart) throw new InvalidDataException($"Unexpected quote on line {line}.");
                quoted = true; atStart = false;
            }
            else { field.Append(c); atStart = false; }
        }
        if (quoted) throw new InvalidDataException("Unterminated quoted field.");
        if (!atStart || closed || cells.Count > 0) Row();
        return new("Delimited", rows.ToImmutable());
    }

    private static ImmutableArray<SourceTable> Workbook(byte[] content)
    {
        using var stream = new MemoryStream(content, false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > 10_000 || archive.Entries.Sum(x => x.Length) > MaximumExpandedBytes
            || archive.Entries.Select(x => x.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count)
            throw new InvalidDataException("Workbook package exceeds bounds or contains duplicate entries.");
        XDocument Xml(string name)
        {
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing workbook part {name}.");
            using var part = entry.Open();
            using var reader = XmlReader.Create(part, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = MaximumExpandedBytes });
            return XDocument.Load(reader);
        }
        var book = Xml("xl/workbook.xml");
        if (book.Root?.Name != S + "workbook") throw new InvalidDataException("Unsupported workbook namespace.");
        var relationships = Xml("xl/_rels/workbook.xml.rels").Root?.Elements().ToDictionary(
            x => (string?)x.Attribute("Id") ?? "", x => x, StringComparer.Ordinal)
            ?? throw new InvalidDataException("Missing workbook relationships.");
        string Text(XElement element) => string.Concat(element.Descendants(S + "t")
            .Where(x => !x.Ancestors(S + "rPh").Any()).Select(x => x.Value));
        string[] shared = archive.GetEntry("xl/sharedStrings.xml") is null ? [] :
            Xml("xl/sharedStrings.xml").Descendants(S + "si").Select(Text).ToArray();
        var tables = ImmutableArray.CreateBuilder<SourceTable>();
        int totalCells = 0;
        foreach (var sheet in book.Descendants(S + "sheet"))
        {
            string name = (string?)sheet.Attribute("name") ?? throw new InvalidDataException("Missing sheet name.");
            if (!relationships.TryGetValue((string?)sheet.Attribute(R + "id") ?? "", out var rel)
                || (string?)rel.Attribute("TargetMode") == "External"
                || !((string?)rel.Attribute("Type") ?? "").EndsWith("/worksheet", StringComparison.Ordinal))
                throw new InvalidDataException("Unsupported worksheet relationship.");
            var target = (string?)rel.Attribute("Target") ?? throw new InvalidDataException("Missing worksheet target.");
            var uri = new Uri(new Uri("https://workbook.invalid/xl/workbook.xml"), target);
            if (uri.Host != "workbook.invalid" || !uri.AbsolutePath.StartsWith("/xl/", StringComparison.Ordinal))
                throw new InvalidDataException("Worksheet target is outside workbook.");
            var rows = ImmutableArray.CreateBuilder<SourceRow>();
            foreach (var row in Xml(Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))).Descendants(S + "sheetData").Elements(S + "row"))
            {
                if (!int.TryParse((string?)row.Attribute("r"), out int number) || number < 1 || number > 1_048_576)
                    throw new InvalidDataException("Invalid worksheet row.");
                var cells = ImmutableArray.CreateBuilder<SourceCell>();
                foreach (var cell in row.Elements(S + "c"))
                {
                    if (++totalCells > MaximumCells) throw new InvalidDataException("Workbook exceeds cell limit.");
                    string reference = (string?)cell.Attribute("r") ?? throw new InvalidDataException("Missing cell reference.");
                    int split = 0, column = 0;
                    while (split < reference.Length && reference[split] is >= 'A' and <= 'Z')
                    { column = checked(column * 26 + reference[split++] - 'A' + 1); }
                    if (column < 1 || column > 16_384 || !int.TryParse(reference[split..], out int cellRow) || cellRow != number)
                        throw new InvalidDataException("Invalid cell reference.");
                    string kind = (string?)cell.Attribute("t") ?? "n";
                    string value = cell.Element(S + "v")?.Value ?? "";
                    if (kind == "s")
                    {
                        if (!int.TryParse(value, out int index) || index < 0 || index >= shared.Length)
                            throw new InvalidDataException("Invalid shared string reference.");
                        value = shared[index]; kind = "text";
                    }
                    else if (kind == "inlineStr") { value = Text(cell); kind = "text"; }
                    else if (kind == "str") kind = "text";
                    cells.Add(new(column, value, kind, cell.Element(S + "f")?.Value, (string?)cell.Attribute("s")));
                }
                if (cells.Select(x => x.Column).Distinct().Count() != cells.Count) throw new InvalidDataException("Duplicate worksheet cell.");
                rows.Add(new(number, cells.ToImmutable()));
            }
            if (rows.Select(x => x.Number).Distinct().Count() != rows.Count) throw new InvalidDataException("Duplicate worksheet row.");
            tables.Add(new(name, rows.ToImmutable()));
        }
        if (tables.Count == 0 || tables.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != tables.Count)
            throw new InvalidDataException("Workbook requires unique worksheets.");
        return tables.ToImmutable();
    }

    private static SourceTable Table(ImportSource source, string table, int header, int first, int last, IEnumerable<int> columns)
    {
        var result = source.Tables.SingleOrDefault(x => x.Name == table) ?? throw new ArgumentException("Choose an existing source table explicitly.");
        int[] mapped = columns.ToArray();
        if (header < 1 || first <= header || last < first || last - (long)first >= AllocationEngine.MaximumAnimals
            || mapped.Any(x => x < 1 || x > 16_384) || mapped.Distinct().Count() != mapped.Length
            || !result.Rows.Any(x => x.Number == header)) throw new ArgumentException("Invalid row range, header or repeated column mapping.");
        return result;
    }

    private static string Value(SourceRow row, int? column, string field, bool identity, bool allowInteger)
    {
        if (!column.HasValue) return "";
        var cell = row.Cells.SingleOrDefault(x => x.Column == column);
        if (cell is null) return "";
        if (cell.Formula is not null) throw new ArgumentException($"{field}: formulas require an explicit values-only source; cached values may be stale.");
        if (cell.Kind is not ("text" or "n")) throw new ArgumentException($"{field}: unsupported cell type {cell.Kind}.");
        if (identity && cell.Kind == "n" && cell.Value.Length > 0)
        {
            if (!allowInteger || !decimal.TryParse(cell.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || number != decimal.Truncate(number) || number < 0 || number > 999_999_999_999_999m)
                throw new ArgumentException($"{field}: store identifiers as text, or explicitly enable integer numeric IDs (maximum 15 digits).");
            return number.ToString("0", CultureInfo.InvariantCulture);
        }
        return cell.Value.Trim();
    }

    private static IEnumerable<SourceRow> SelectedRows(ImportSource source, SourceTable table, int first, int last)
    {
        if (source.Format is ".csv" or ".tsv")
        {
            // Delimited records can span physical lines. Only record starts are source rows.
            if (!table.Rows.Any(x => x.Number == first) || !table.Rows.Any(x => x.Number == last))
                throw new ArgumentException("Choose first/last record start lines explicitly.");
            return table.Rows.Where(x => x.Number >= first && x.Number <= last);
        }
        var rows = table.Rows.ToDictionary(x => x.Number);
        return Enumerable.Range(first, last - first + 1).Select(i => rows.GetValueOrDefault(i) ?? new SourceRow(i, []));
    }

    public static ImportPreview<AnimalImport> Animals(ImportSource source, string table, int header, int first, int last,
        AnimalColumns columns, string weightUnit = "g", bool demonstration = false, bool allowIntegerIds = false)
    {
        var mapping = new List<ColumnMapping> { new("AnimalId", columns.Identity), new("Weight", columns.Weight) };
        void Add(string field, int? column) { if (column.HasValue) mapping.Add(new(field, column.Value)); }
        Add("Cohort", columns.Cohort); Add("OldLocation", columns.OldLocation); Add("MaternalId", columns.MaternalId);
        Add("PaternalId", columns.PaternalId); Add("Notes", columns.Notes);
        var data = Table(source, table, header, first, last, mapping.Select(x => x.Column));
        if (weightUnit is not ("g" or "kg")) throw new ArgumentException("Weight unit must be explicitly g or kg.");
        var application = new ImportApplication(Guid.NewGuid().ToString("N"), source.Id, table, header, first, last,
            weightUnit, mapping.ToImmutableArray(), allowIntegerIds, demonstration);
        var animals = ImmutableArray.CreateBuilder<Animal>(); var details = ImmutableArray.CreateBuilder<AnimalDetails>();
        var issues = ImmutableArray.CreateBuilder<ImportIssue>();
        var identities = new Dictionary<(string, string), int>();
        foreach (var row in SelectedRows(source, data, first, last))
        {
            // A chosen range is exact: missing and blank rows cannot silently disappear.
            int i = row.Number;
            try
            {
                string Get(int? col, string field, bool id = false) => Value(row, col, field, id, allowIntegerIds);
                string id = Get(columns.Identity, "AnimalId", true), cohort = Get(columns.Cohort, "Cohort", true);
                if (id.Length == 0) throw new ArgumentException("AnimalId: required.");
                string rawWeight = Get(columns.Weight, "Weight");
                if (!decimal.TryParse(rawWeight, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture, out var weight)) throw new ArgumentException("Weight: require a finite decimal using '.'; thousands separators are unsupported.");
                weight = checked(weight * (weightUnit == "kg" ? 1000m : 1m));
                if (weight <= 0 || weight > 1_000_000m) throw new ArgumentException("Weight: require > 0 and <= 1,000,000 grams.");
                if (!demonstration && identities.TryGetValue((cohort, id), out int previous))
                    throw new ArgumentException($"AnimalId: duplicate within cohort, also at row {previous}; correct the source or map an explicit cohort.");
                identities.TryAdd((cohort, id), i);
                string record = Guid.NewGuid().ToString("N");
                animals.Add(new(record, id, weight, cohort, Get(columns.OldLocation, "OldLocation")));
                details.Add(new(record, new(application.Id, i), Get(columns.MaternalId, "MaternalId", true),
                    Get(columns.PaternalId, "PaternalId", true), Get(columns.Notes, "Notes")));
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException)
            { issues.Add(new(i, ex.Message.Split(':')[0], ex.Message)); }
        }
        return issues.Count > 0 ? new(null, issues.ToImmutable()) : new(new(application, animals.ToImmutable(), details.ToImmutable()), []);
    }

    public static ImportPreview<TreatmentImport> Treatments(ImportSource source, string table, int header, int first, int last,
        TreatmentColumns columns, bool allowIntegerIds = false)
    {
        var mapping = new List<ColumnMapping> { new("TreatmentId", columns.Identity), new("Label", columns.Label) };
        var factors = columns.Factors.IsDefault ? ImmutableArray<int>.Empty : columns.Factors;
        mapping.AddRange(factors.Select((x, i) => new ColumnMapping($"Factor{i + 1}", x)));
        var data = Table(source, table, header, first, last, mapping.Select(x => x.Column));
        var application = new ImportApplication(Guid.NewGuid().ToString("N"), source.Id, table, header, first, last, "",
            mapping.ToImmutableArray(), allowIntegerIds);
        var treatments = ImmutableArray.CreateBuilder<Treatment>(); var details = ImmutableArray.CreateBuilder<TreatmentDetails>();
        var issues = ImmutableArray.CreateBuilder<ImportIssue>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in SelectedRows(source, data, first, last))
        {
            int i = row.Number;
            try
            {
                string id = Value(row, columns.Identity, "TreatmentId", true, allowIntegerIds);
                string label = Value(row, columns.Label, "Label", true, allowIntegerIds);
                if (id.Length == 0 || label.Length == 0 || !ids.Add(id) || !labels.Add(label))
                    throw new ArgumentException("Treatment: require unique nonblank IDs and labels.");
                foreach (int factor in factors)
                    if (Value(row, factor, "Factor", true, allowIntegerIds).Length == 0) throw new ArgumentException("Factor: required.");
                treatments.Add(new(id, label)); details.Add(new(id, new(application.Id, i), mapping.Skip(2).ToImmutableArray()));
            }
            catch (ArgumentException ex) { issues.Add(new(i, ex.Message.Split(':')[0], ex.Message)); }
        }
        return issues.Count > 0 ? new(null, issues.ToImmutable()) : new(new(application, treatments.ToImmutable(), details.ToImmutable()), []);
    }
}
