using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PoultryDesigner.Engine;

public sealed record OpenedProject(ProjectSession Session, string FileDigest);

public static class ProjectStore
{
    public const int SchemaVersion = 1;
    public const string ProjectExtension = ".experimentproj";
    public const string LegacyExtension = ".poultryproj";
    public const long MaximumProjectBytes = 256L * 1024 * 1024;
    private const string Format = "PoultryDesigner.Project";
    private sealed record Envelope(string Format, int SchemaVersion, string PayloadSha256, ProjectDocument Project);
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info => {
            if (info.Kind == JsonTypeInfoKind.Object && info.Type.Namespace == typeof(ProjectDocument).Namespace)
                foreach (var property in info.Properties)
                    property.IsRequired = !(info.Type == typeof(ProjectInputs) && property.Name is nameof(ProjectInputs.PreparedLayout) or nameof(ProjectInputs.AutomaticPoolSelection));
        });
        return new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true, MaxDepth = 64, TypeInfoResolver = resolver };
    }

    // Hashes detect accidental corruption. They are not signatures or protection against deliberate rewriting.
    public static ProjectDocument Load(string path, CancellationToken cancellation = default) => LoadCore(path, cancellation, out _);
    private static ProjectDocument LoadCore(string path, CancellationToken cancellation, out string fileDigest)
    {
        using var file = File.OpenRead(path);
        if (file.Length > MaximumProjectBytes) throw new InvalidDataException("Project exceeds 256 MiB.");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            using var json = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = 64 });
            void Unique(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property."); Unique(property.Value); }
                }
                else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Unique(item);
            }
            Unique(json.RootElement);
            var envelope = json.Deserialize<Envelope>(Options) ?? throw new InvalidDataException("Empty project.");
            if (envelope.Format != Format || envelope.SchemaVersion != SchemaVersion || envelope.Project.SchemaVersion != SchemaVersion)
                throw new InvalidDataException("Unsupported project format/schema. No migration is available yet; keep the original file.");
            if (ProjectSession.Digest(envelope.Project) != envelope.PayloadSha256) throw new InvalidDataException("Project payload checksum mismatch.");
            ProjectValidation.Validate(envelope.Project, cancellation);
            // Keep this read handle open: the digest belongs to the bytes actually opened,
            // even if the path is replaced by another process immediately afterwards.
            file.Position = 0;
            fileDigest = Convert.ToHexString(SHA256.HashData(file));
            return envelope.Project;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException
            or NullReferenceException or OverflowException or System.Xml.XmlException or KeyNotFoundException)
        { throw new InvalidDataException("Malformed or inconsistent project: " + ex.Message, ex); }
    }

    public static ProjectSession Open(string path, CancellationToken cancellation = default) => OpenWithDigest(path, cancellation).Session;
    public static OpenedProject OpenWithDigest(string path, CancellationToken cancellation = default)
    {
        var document = LoadCore(path, cancellation, out var digest);
        return new(ProjectSession.RestoreValidated(document), digest);
    }
    public static string FileDigest(string path)
    { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }

    // The optional file digest detects an externally changed target. The caller keeps it after open/save.
    // An exclusive lock file also prevents this writer from racing another cooperating writer.
    public static string Save(ProjectDocument project, string path, CancellationToken cancellation = default,
        string? expectedFileDigest = null)
    {
        if (!string.Equals(Path.GetExtension(path), ProjectExtension, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetExtension(path), LegacyExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use the " + ProjectExtension + " extension; source workbooks cannot be save targets.");
        ProjectValidation.Validate(project, cancellation);
        string target = Path.GetFullPath(path);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string lockPath = target + ".lock";
        using var writerLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            1, FileOptions.DeleteOnClose);
        if (File.Exists(target))
        {
            if (expectedFileDigest is not null && FileDigest(target) != expectedFileDigest)
                throw new IOException("Project changed on disk. Open the newer file or save to a new path.");
            if (Load(target, cancellation).Id != project.Id) throw new IOException("Target belongs to another project. Save to a new path.");
        }
        else if (expectedFileDigest is not null) throw new IOException("Expected project target is missing.");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(Format, SchemaVersion, ProjectSession.Digest(project), project), Options);
            if (bytes.LongLength > MaximumProjectBytes) throw new InvalidDataException("Project exceeds 256 MiB.");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(true); }
            cancellation.ThrowIfCancellationRequested();
            // No delete-then-write fallback: failure preserves the existing project and previous backup.
            if (File.Exists(target)) File.Replace(temporary, target, target + ".bak", true);
            else File.Move(temporary, target);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ProjectDocument LoadBackup(string path, CancellationToken cancellation = default) => Load(path + ".bak", cancellation);
}
