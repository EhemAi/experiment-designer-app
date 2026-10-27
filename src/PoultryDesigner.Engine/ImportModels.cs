using System.Collections.Immutable;

namespace PoultryDesigner.Engine;

// Source bytes and every cell (including unmapped columns) remain available for audit.
public sealed record SourceCell(int Column, string Value, string Kind = "text", string? Formula = null, string? Style = null);
public sealed record SourceRow(int Number, ImmutableArray<SourceCell> Cells);
public sealed record SourceTable(string Name, ImmutableArray<SourceRow> Rows);
public sealed record ImportSource(string Id, string FileName, string OriginalPath, string Sha256,
    string ContentBase64, string Format, DateTimeOffset CapturedUtc, ImmutableArray<SourceTable> Tables);
public sealed record ColumnMapping(string Field, int Column);
public sealed record ImportApplication(string Id, string SourceId, string Table, int HeaderRow,
    int FirstRow, int LastRow, string WeightUnit, ImmutableArray<ColumnMapping> Columns,
    bool AllowIntegerIds = false, bool Demonstration = false);
public sealed record RowOrigin(string ApplicationId, int Row);
public sealed record AnimalDetails(string RecordId, RowOrigin? Origin = null,
    string MaternalId = "", string PaternalId = "", string Notes = "");
public sealed record TreatmentDetails(string TreatmentId, RowOrigin? Origin = null,
    ImmutableArray<ColumnMapping> FactorColumns = default);
public sealed record AnimalImport(ImportApplication Application, ImmutableArray<Animal> Animals,
    ImmutableArray<AnimalDetails> Details);
public sealed record TreatmentImport(ImportApplication Application, ImmutableArray<Treatment> Treatments,
    ImmutableArray<TreatmentDetails> Details);
public sealed record ImportIssue(int Row, string Field, string Message);
public sealed record ImportPreview<T>(T? Value, ImmutableArray<ImportIssue> Issues) where T : class
{
    public bool Success => Value is not null && Issues.IsEmpty;
}
public sealed record AnimalColumns(int Identity, int Weight, int? Cohort = null, int? OldLocation = null,
    int? MaternalId = null, int? PaternalId = null, int? Notes = null);
public sealed record TreatmentColumns(int Identity, int Label, ImmutableArray<int> Factors = default);
