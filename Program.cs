using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

// ===== [ADD] Plugins: using =====
using Contracts;
using BlazorPdfApp.Hosting; // ปรับให้ตรงกับ namespace ของ PluginLoader/PluginManifest ของคุณ

const long MaxUploadBytes = 50L * 1024 * 1024; // 50 MB upload limit per PDF

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<List<IBlazorPlugin>>(); // bucket UI plugins
builder.Services.AddHttpClient();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes;
});

// ===== [ADD] Plugins: DI bucket สำหรับปลั๊กอินที่มี UI =====
builder.Services.AddSingleton<List<IBlazorPlugin>>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// ====== PDF browser configuration ======
// TODO: Replace the UNC path below with the actual PDF root if it differs in your environment.
//       Ensure the web process identity has READ/WRITE access on both the share and NTFS ACLs.
var pdfRoot = builder.Configuration["PdfStorage:Root"]
              ?? @"\\\\10.192.132.91\\PdfRoot";

if (!Directory.Exists(pdfRoot))
{
    app.Logger.LogWarning("Configured PDF root '{PdfRoot}' is not accessible. Confirm the share path and permissions.", pdfRoot);
}

var allowedLines = new[] { "F1", "F2", "F3" };
var pdfService = new PdfBrowserService(pdfRoot, allowedLines, MaxUploadBytes, app.Logger);

app.MapGet("/api/folders", () => pdfService.GetExistingLines());
app.MapGet("/api/folders/{line}", (string line, HttpRequest request)
        => pdfService.GetFolderListing(line, request.Query["path"].ToString()));
app.MapGet("/pdf/{line}/{file}", (string line, string file, HttpRequest request)
        => pdfService.GetPdfStream(line, file, request.Query["path"].ToString()));
app.MapGet("/api/edit-status", () => pdfService.GetEditStatuses());
app.MapPost("/api/folders/{line}/upload", async (string line, HttpRequest request)
        => await pdfService.UploadPdfAsync(line, request));
app.MapPost("/api/folders/{line}/subfolders", async (string line, HttpContext context)
        => await pdfService.CreateSubfolderAsync(line, context));
// ====== /PDF browser configuration ======

// ===== [ADD] Plugins: โหลดปลั๊กอินจากโฟลเดอร์ "Plugins" =====
using (var scope = app.Services.CreateScope())
{
    var sp  = scope.ServiceProvider;
    var services = scope.ServiceProvider;
    var env = services.GetRequiredService<IWebHostEnvironment>();
    var pluginsDir = Path.Combine(env.ContentRootPath, "Plugins");

    var loaded = PluginLoader.LoadAll(services, pluginsDir);

    var uiBucket = services.GetRequiredService<List<IBlazorPlugin>>();
    foreach (var d in loaded)
    {
        _ = d.Instance.ExecuteAsync();          // งาน background ถ้ามี
        if (d.Blazor is not null) uiBucket.Add(d.Blazor);
    }
}
// ===== [/ADD] Plugins =====

app.MapRazorComponents<BlazorPdfApp.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();

internal sealed record FolderListing(
    string Line,
    IReadOnlyList<string> PathSegments,
    IReadOnlyList<string> Folders,
    IReadOnlyList<string> Files,
    IReadOnlyList<FolderDocument> Documents);
internal sealed record FolderDocument(string BaseName, IReadOnlyList<PdfVersion> Versions);
internal sealed record PdfVersion(string FileName, int Division, DateTime UploadedUtc, string Comment);
internal sealed record CreateFolderRequest(string? Name);
internal sealed record LineEditStatus(string Line, BranchEditStatus? Root, string? ErrorMessage);
internal sealed record BranchEditStatus(
    string Name,
    IReadOnlyList<string> PathSegments,
    int PdfCount,
    int TotalPdfCount,
    DateTime? LastModifiedUtc,
    string Status,
    IReadOnlyList<FileEditStatus> RecentFiles,
    IReadOnlyList<DocumentEditStatus> Documents,
    IReadOnlyList<BranchEditStatus> Children,
    string? ErrorMessage);
internal sealed record FileEditStatus(string FileName, DateTime LastModifiedUtc, long SizeBytes, string RelativePath);
internal sealed record DocumentEditStatus(string BaseName, DateTime? LatestUploadedUtc, IReadOnlyList<PdfVersionStatus> Versions);
internal sealed record PdfVersionStatus(string FileName, int Division, DateTime UploadedUtc, string Comment, string RelativePath);
internal sealed class PdfMetadata
{
    public Dictionary<string, List<PdfVersionMetadata>> Documents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class PdfVersionMetadata
{
    public string FileName { get; set; } = string.Empty;
    public int Division { get; set; }
    public DateTime UploadedUtc { get; set; }
    public string Comment { get; set; } = string.Empty;
}

internal sealed class PdfBrowserService
{
    private readonly string _pdfRoot;
    private readonly HashSet<string> _allowedLines;
    private readonly long _maxUploadBytes;
    private readonly ILogger _logger;
    private const string MetadataFileName = ".pdf-metadata.json";
    private const int MaxCommentLength = 500;
    private static readonly JsonSerializerOptions MetadataSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal PdfBrowserService(string pdfRoot, IEnumerable<string> allowedLines, long maxUploadBytes, ILogger logger)
    {
        _pdfRoot = pdfRoot;
        _allowedLines = new HashSet<string>(allowedLines, StringComparer.OrdinalIgnoreCase);
        _maxUploadBytes = maxUploadBytes;
        _logger = logger;
    }

    internal IResult GetExistingLines()
    {
        try
        {
            var existing = _allowedLines
                .Select(line => new { line, path = Path.Combine(_pdfRoot, line) })
                .Where(x => Directory.Exists(x.path))
                .Select(x => x.line)
                .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(existing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to enumerate folders under {PdfRoot}", _pdfRoot);
            return Results.Problem("ไม่สามารถอ่านรายการโฟลเดอร์ได้ กรุณาตรวจสอบการแชร์และสิทธิ์การเข้าถึง");
        }
    }

    internal IResult GetFolderListing(string line, string? rawPath)
    {
        var pathSegments = ParsePathSegments(rawPath);
        if (!TryResolveDirectory(line, pathSegments, out var directoryPath, out var normalizedSegments, out var error))
        {
            return error ?? Results.NotFound("Unknown folder");
        }

        try
        {
            var folders = Directory.EnumerateDirectories(directoryPath!, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name is not null && IsValidFolderName(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var files = Directory.EnumerateFiles(directoryPath!, "*.pdf", SearchOption.TopDirectoryOnly)
                .Where(path => IsValidPdfFileName(Path.GetFileName(path)))
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var metadata = LoadMetadata(directoryPath!);
            var metadataChanged = PruneMetadata(metadata, files);
            var documents = BuildDocumentListing(directoryPath!, files, metadata);

            if (metadataChanged)
            {
                TryPersistMetadata(directoryPath!, metadata);
            }

            return Results.Ok(new FolderListing(
                line,
                normalizedSegments ?? new List<string>(),
                folders,
                files,
                documents));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to enumerate files for {Line} in {PdfRoot}", line, _pdfRoot);
            return Results.Problem("ไม่สามารถอ่านไฟล์ในโฟลเดอร์ที่เลือกได้ กรุณาตรวจสอบสิทธิ์การเข้าถึง");
        }
    }

    internal IResult GetPdfStream(string line, string file, string? rawPath)
    {
        var pathSegments = ParsePathSegments(rawPath);
        if (TryResolvePdf(line, pathSegments, file, out var filePath) is { } error)
        {
            return error;
        }

        try
        {
            var stream = File.OpenRead(filePath!);
            return Results.File(stream, MediaTypeNames.Application.Pdf, enableRangeProcessing: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to open PDF {File} for line {Line}", file, line);
            return Results.Problem("ไม่สามารถเปิดไฟล์ PDF ได้ กรุณาตรวจสอบการแชร์และสิทธิ์การเข้าถึง");
        }
    }

    internal IResult GetEditStatuses()
    {
        var statuses = _allowedLines
            .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
            .Select(BuildLineEditStatus)
            .ToList();

        return Results.Ok(statuses);
    }

    internal async Task<IResult> UploadPdfAsync(string line, HttpRequest request)
    {
        var pathSegments = ParsePathSegments(request.Query["path"].ToString());
        if (!TryResolveDirectory(line, pathSegments, out var directoryPath, out _, out var pathError))
        {
            return pathError ?? Results.NotFound("Unknown folder");
        }

        if (!request.HasFormContentType)
        {
            return Results.BadRequest("ต้องเป็น multipart/form-data");
        }

        try
        {
            var form = await request.ReadFormAsync();
            var formFile = form.Files.GetFile("file");

            if (formFile is null || formFile.Length == 0)
            {
                return Results.BadRequest("ไม่พบไฟล์หรือไฟล์ว่าง");
            }

            var originalName = Path.GetFileName(formFile.FileName);
            if (!IsValidPdfFileName(originalName))
            {
                return Results.BadRequest("รองรับเฉพาะไฟล์ .pdf เท่านั้น");
            }

            if (formFile.Length > _maxUploadBytes)
            {
                return Results.BadRequest($"ไฟล์มีขนาดเกิน {_maxUploadBytes / (1024 * 1024)} MB");
            }

            var commentRaw = form["comment"].ToString();
            if (string.IsNullOrWhiteSpace(commentRaw))
            {
                return Results.BadRequest("กรุณากรอกคอมเมนต์เพื่อบันทึกการอัปเดต");
            }

            var comment = commentRaw.Trim();
            if (comment.Length > MaxCommentLength)
            {
                return Results.BadRequest($"คอมเมนต์ยาวเกินไป (สูงสุด {MaxCommentLength} ตัวอักษร)");
            }

            var metadata = LoadMetadata(directoryPath!);
            var baseName = NormalizeBaseName(Path.GetFileNameWithoutExtension(originalName));
            var nextDivision = GetNextDivisionNumber(directoryPath!, baseName, metadata);
            var storedFileName = $"{baseName}_Division{nextDivision:D2}.pdf";
            var destination = Path.Combine(directoryPath!, storedFileName);

            if (File.Exists(destination))
            {
                return Results.Conflict("ไฟล์เวอร์ชันนี้มีอยู่แล้ว");
            }

            await using var readStream = formFile.OpenReadStream();
            await using var writeStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await readStream.CopyToAsync(writeStream);

            var now = DateTime.UtcNow;

            if (!metadata.Documents.TryGetValue(baseName, out var versions))
            {
                versions = new List<PdfVersionMetadata>();
                metadata.Documents[baseName] = versions;
            }

            versions.RemoveAll(v => string.Equals(v.FileName, storedFileName, StringComparison.OrdinalIgnoreCase));
            versions.Add(new PdfVersionMetadata
            {
                FileName = storedFileName,
                Division = nextDivision,
                UploadedUtc = now,
                Comment = comment
            });
            versions.Sort((left, right) => left.Division.CompareTo(right.Division));

            SaveMetadata(directoryPath!, metadata);

            return Results.Ok(new
            {
                file = storedFileName,
                baseName,
                division = nextDivision,
                comment,
                uploadedUtc = now,
                path = pathSegments
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to upload PDF to {Line}", line);
            return Results.Problem("ไม่สามารถอัปโหลดไฟล์ได้ กรุณาตรวจสอบสิทธิ์การเข้าถึง");
        }
    }

    internal async Task<IResult> CreateSubfolderAsync(string line, HttpContext context)
    {
        var pathSegments = ParsePathSegments(context.Request.Query["path"].ToString());
        if (!TryResolveDirectory(line, pathSegments, out var directoryPath, out _, out var error))
        {
            return error ?? Results.NotFound("Unknown folder");
        }

        CreateFolderRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<CreateFolderRequest>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Invalid folder creation payload for line {Line}", line);
            return Results.BadRequest("รูปแบบคำขอไม่ถูกต้อง");
        }

        var name = request?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.BadRequest("กรุณาระบุชื่อโฟลเดอร์");
        }

        if (!IsValidFolderName(name))
        {
            return Results.BadRequest("ชื่อโฟลเดอร์ไม่ถูกต้อง");
        }

        var targetPath = Path.Combine(directoryPath!, name);

        if (Directory.Exists(targetPath))
        {
            return Results.Conflict("มีโฟลเดอร์ชื่อนี้อยู่แล้ว");
        }

        try
        {
            Directory.CreateDirectory(targetPath);
            return Results.Ok(new { folder = name, path = pathSegments });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to create subfolder {Folder} for line {Line}", name, line);
            return Results.Problem("ไม่สามารถสร้างโฟลเดอร์ได้ กรุณาตรวจสอบสิทธิ์การเข้าถึง");
        }
    }

    private PdfMetadata LoadMetadata(string directoryPath)
    {
        var metadataPath = Path.Combine(directoryPath, MetadataFileName);

        if (!File.Exists(metadataPath))
        {
            return new PdfMetadata();
        }

        try
        {
            var json = File.ReadAllText(metadataPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new PdfMetadata();
            }

            var metadata = JsonSerializer.Deserialize<PdfMetadata>(json, MetadataSerializerOptions) ?? new PdfMetadata();
            NormalizeMetadata(metadata);
            return metadata;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to load metadata for directory {Directory}", directoryPath);
            return new PdfMetadata();
        }
    }

    private static void NormalizeMetadata(PdfMetadata metadata)
    {
        if (metadata.Documents is null)
        {
            metadata.Documents = new Dictionary<string, List<PdfVersionMetadata>>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        if (metadata.Documents.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            metadata.Documents = new Dictionary<string, List<PdfVersionMetadata>>(metadata.Documents, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var key in metadata.Documents.Keys.ToList())
        {
            var versions = metadata.Documents[key] ?? new List<PdfVersionMetadata>();
            var normalized = versions
                .Where(v => v is not null && !string.IsNullOrWhiteSpace(v.FileName))
                .Select(v =>
                {
                    v.Comment ??= string.Empty;
                    return v;
                })
                .ToList();

            metadata.Documents[key] = normalized;
        }
    }

    private static bool PruneMetadata(PdfMetadata metadata, IReadOnlyCollection<string> currentFiles)
    {
        if (metadata.Documents.Count == 0)
        {
            return false;
        }

        var fileSet = new HashSet<string>(currentFiles, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var key in metadata.Documents.Keys.ToList())
        {
            var versions = metadata.Documents[key];
            var filtered = versions
                .Where(v => fileSet.Contains(v.FileName))
                .ToList();

            if (filtered.Count != versions.Count)
            {
                changed = true;
                if (filtered.Count == 0)
                {
                    metadata.Documents.Remove(key);
                }
                else
                {
                    metadata.Documents[key] = filtered;
                }
            }
        }

        return changed;
    }

    private IReadOnlyList<FolderDocument> BuildDocumentListing(string directoryPath, IReadOnlyCollection<string> files, PdfMetadata metadata)
    {
        var documents = new Dictionary<string, List<PdfVersion>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string baseName;
            PdfVersionMetadata? metadataEntry = null;

            if (TryGetMetadataEntry(metadata, file, out var metadataBase, out metadataEntry))
            {
                baseName = NormalizeBaseName(metadataBase);
            }
            else if (TryParseDivisionSuffix(file, out var parsedBase, out _))
            {
                baseName = NormalizeBaseName(parsedBase);
            }
            else
            {
                baseName = NormalizeBaseName(Path.GetFileNameWithoutExtension(file));
            }

            var division = metadataEntry?.Division ?? 0;
            if (division == 0 && TryParseDivisionSuffix(file, out var parsedBaseName, out var parsedDivision)
                && string.Equals(parsedBaseName, baseName, StringComparison.OrdinalIgnoreCase))
            {
                division = parsedDivision;
            }

            var filePath = Path.Combine(directoryPath, file);
            var uploadedUtc = metadataEntry?.UploadedUtc ?? GetLastWriteTimeUtcSafe(filePath);
            var comment = metadataEntry?.Comment ?? string.Empty;

            if (!documents.TryGetValue(baseName, out var versionList))
            {
                versionList = new List<PdfVersion>();
                documents[baseName] = versionList;
            }

            if (versionList.Any(v => string.Equals(v.FileName, file, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            versionList.Add(new PdfVersion(file, division, uploadedUtc, comment));
        }

        foreach (var list in documents.Values)
        {
            list.Sort((left, right) =>
            {
                var divisionComparison = left.Division.CompareTo(right.Division);
                if (divisionComparison != 0)
                {
                    return divisionComparison;
                }

                return string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);
            });
        }

        return documents
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new FolderDocument(kvp.Key, kvp.Value))
            .ToList();
    }

    private static bool TryGetMetadataEntry(PdfMetadata metadata, string fileName, out string baseName, out PdfVersionMetadata? entry)
    {
        foreach (var kvp in metadata.Documents)
        {
            var match = kvp.Value.FirstOrDefault(v => string.Equals(v.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                baseName = kvp.Key;
                entry = match;
                return true;
            }
        }

        baseName = string.Empty;
        entry = null;
        return false;
    }

    private void TryPersistMetadata(string directoryPath, PdfMetadata metadata)
    {
        try
        {
            SaveMetadata(directoryPath, metadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to persist metadata for {Directory}", directoryPath);
        }
    }

    private void SaveMetadata(string directoryPath, PdfMetadata metadata)
    {
        NormalizeMetadata(metadata);
        var metadataPath = Path.Combine(directoryPath, MetadataFileName);
        var json = JsonSerializer.Serialize(metadata, MetadataSerializerOptions);
        File.WriteAllText(metadataPath, json, Encoding.UTF8);
    }

    private DateTime GetLastWriteTimeUtcSafe(string fullPath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to read last write time for {File}", fullPath);
            return DateTime.UtcNow;
        }
    }

    private static string NormalizeBaseName(string rawBaseName)
    {
        if (string.IsNullOrWhiteSpace(rawBaseName))
        {
            return "Document";
        }

        var stripped = StripDivisionSuffix(rawBaseName.Trim());
        return SanitizeFileNameComponent(stripped);
    }

    private static string StripDivisionSuffix(string baseName)
    {
        const string marker = "_Division";
        var index = baseName.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var suffix = baseName[(index + marker.Length)..];
            if (int.TryParse(suffix, out _))
            {
                return baseName[..index];
            }
        }

        return baseName;
    }

    private static string SanitizeFileNameComponent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Document";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var sanitized = builder.ToString().Trim('_', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "Document" : sanitized;
    }

    private static bool TryParseDivisionSuffix(string fileName, out string baseName, out int division)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        const string marker = "_Division";
        var index = nameWithoutExtension.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var suffix = nameWithoutExtension[(index + marker.Length)..];
            if (int.TryParse(suffix, out var parsedDivision) && parsedDivision > 0)
            {
                baseName = nameWithoutExtension[..index];
                division = parsedDivision;
                return true;
            }
        }

        baseName = nameWithoutExtension;
        division = 0;
        return false;
    }

    private int GetNextDivisionNumber(string directoryPath, string baseName, PdfMetadata metadata)
    {
        var maxDivision = 0;

        if (metadata.Documents.TryGetValue(baseName, out var versions) && versions.Count > 0)
        {
            maxDivision = Math.Max(maxDivision, versions.Max(v => v.Division));
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            if (fileName is null)
            {
                continue;
            }

            if (TryParseDivisionSuffix(fileName, out var parsedBase, out var parsedDivision)
                && string.Equals(parsedBase, baseName, StringComparison.OrdinalIgnoreCase))
            {
                maxDivision = Math.Max(maxDivision, parsedDivision);
            }
        }

        return maxDivision + 1;
    }

    private bool TryResolveDirectory(string line, IReadOnlyList<string> segments, out string? directoryPath, out List<string>? normalizedSegments, out IResult? error)
    {
        directoryPath = null;
        normalizedSegments = null;
        error = null;

        if (!TryResolveLine(line, out var linePath))
        {
            error = Results.NotFound("Unknown folder");
            return false;
        }

        var currentDirectory = new DirectoryInfo(linePath!);
        var collected = new List<string>();

        foreach (var segment in segments)
        {
            if (!IsValidFolderName(segment))
            {
                error = Results.BadRequest("ชื่อโฟลเดอร์ไม่ถูกต้อง");
                return false;
            }

            var nextPath = Path.Combine(currentDirectory.FullName, segment);
            if (!Directory.Exists(nextPath))
            {
                error = Results.NotFound("ไม่พบโฟลเดอร์ที่ระบุ");
                return false;
            }

            currentDirectory = new DirectoryInfo(nextPath);
            collected.Add(currentDirectory.Name);
        }

        directoryPath = currentDirectory.FullName;
        normalizedSegments = collected;
        return true;
    }

    private bool TryResolveLine(string line, out string? linePath)
    {
        linePath = null;
        if (!_allowedLines.Contains(line))
        {
            return false;
        }

        var candidate = Path.Combine(_pdfRoot, line);
        if (!Directory.Exists(candidate))
        {
            return false;
        }

        linePath = candidate;
        return true;
    }

    private IResult? TryResolvePdf(string line, IReadOnlyList<string> pathSegments, string file, out string? filePath)
    {
        filePath = null;
        if (!IsValidPdfFileName(file))
        {
            return Results.BadRequest("Invalid PDF file name");
        }

        if (!TryResolveDirectory(line, pathSegments, out var directoryPath, out _, out var error))
        {
            return error;
        }

        var candidate = Path.Combine(directoryPath!, file);
        if (!File.Exists(candidate))
        {
            return Results.NotFound();
        }

        filePath = candidate;
        return null;
    }

    private static IReadOnlyList<string> ParsePathSegments(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return Array.Empty<string>();
        }

        return rawPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsValidPdfFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            return false;
        }

        return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static bool IsValidFolderName(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        var trimmed = folderName.Trim();
        if (trimmed.Length > 100)
        {
            return false;
        }

        if (string.Equals(trimmed, ".", StringComparison.Ordinal) || string.Equals(trimmed, "..", StringComparison.Ordinal))
        {
            return false;
        }

        return trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
               && !trimmed.Contains(Path.DirectorySeparatorChar)
               && !trimmed.Contains(Path.AltDirectorySeparatorChar);
    }

    private static string BuildRelativeFilePath(IReadOnlyList<string> segments, string fileName)
    {
        if (segments.Count == 0)
        {
            return fileName;
        }

        return string.Join('/', segments) + "/" + fileName;
    }

    private static string DescribeRelativeTime(DateTime timestampUtc)
    {
        var now = DateTime.UtcNow;
        var delta = now - timestampUtc;

        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMinutes < 1)
        {
            return "เมื่อสักครู่";
        }

        if (delta.TotalHours < 1)
        {
            return $"ประมาณ {Math.Floor(delta.TotalMinutes)} นาทีที่แล้ว";
        }

        if (delta.TotalDays < 1)
        {
            return $"ประมาณ {Math.Floor(delta.TotalHours)} ชั่วโมงที่แล้ว";
        }

        if (delta.TotalDays < 7)
        {
            return $"ประมาณ {Math.Floor(delta.TotalDays)} วันที่แล้ว";
        }

        if (delta.TotalDays < 30)
        {
            return $"ประมาณ {Math.Floor(delta.TotalDays / 7)} สัปดาห์ที่แล้ว";
        }

        if (delta.TotalDays < 365)
        {
            return $"ประมาณ {Math.Floor(delta.TotalDays / 30)} เดือนที่แล้ว";
        }

        return $"ประมาณ {Math.Floor(delta.TotalDays / 365)} ปีที่แล้ว";
    }

    private BranchEditStatus BuildBranchStatus(DirectoryInfo directory, IReadOnlyList<string> segments)
    {
        List<FileInfo> pdfFiles;
        try
        {
            pdfFiles = directory.EnumerateFiles("*.pdf", SearchOption.TopDirectoryOnly)
                .Where(file => IsValidPdfFileName(file.Name))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BranchEditStatus(
                directory.Name,
                segments.ToList(),
                PdfCount: 0,
                TotalPdfCount: 0,
                LastModifiedUtc: null,
                Status: "ไม่สามารถอ่านไฟล์ในกิ่งนี้ได้",
                RecentFiles: Array.Empty<FileEditStatus>(),
                Documents: Array.Empty<DocumentEditStatus>(),
                Children: Array.Empty<BranchEditStatus>(),
                ErrorMessage: $"ไม่สามารถอ่านไฟล์: {ex.Message}");
        }

        var metadata = LoadMetadata(directory.FullName);
        var fileNames = pdfFiles.Select(file => file.Name).ToList();
        var metadataChanged = PruneMetadata(metadata, fileNames);

        var documentListings = BuildDocumentListing(directory.FullName, fileNames, metadata);
        var documentStatuses = documentListings
            .Select(doc =>
            {
                var versions = doc.Versions
                    .OrderByDescending(v => v.UploadedUtc)
                    .ThenByDescending(v => v.Division)
                    .Select(v => new PdfVersionStatus(
                        v.FileName,
                        v.Division,
                        v.UploadedUtc,
                        v.Comment ?? string.Empty,
                        BuildRelativeFilePath(segments, v.FileName)))
                    .ToList();

                DateTime? latest = versions.Count > 0
                    ? versions[0].UploadedUtc
                    : (DateTime?)null;

                return new DocumentEditStatus(doc.BaseName, latest, versions);
            })
            .OrderByDescending(d => d.LatestUploadedUtc ?? DateTime.MinValue)
            .ThenBy(d => d.BaseName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (metadataChanged)
        {
            TryPersistMetadata(directory.FullName, metadata);
        }

        var recentFiles = pdfFiles
            .Take(5)
            .Select(file => new FileEditStatus(
                file.Name,
                file.LastWriteTimeUtc,
                file.Length,
                BuildRelativeFilePath(segments, file.Name)))
            .ToList();

        var children = new List<BranchEditStatus>();
        string? childEnumerationError = null;

        try
        {
            foreach (var childDirectory in directory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                         .Where(dir => IsValidFolderName(dir.Name))
                         .OrderBy(dir => dir.Name, StringComparer.OrdinalIgnoreCase))
            {
                var childSegments = new List<string>(segments) { childDirectory.Name };
                children.Add(BuildBranchStatus(childDirectory, childSegments));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            childEnumerationError = $"ไม่สามารถอ่านโฟลเดอร์ย่อย: {ex.Message}";
        }

        var pdfCount = pdfFiles.Count;
        var totalPdfCount = pdfCount + children.Sum(child => child.TotalPdfCount);

        DateTime? lastModified = pdfFiles.Count > 0
            ? pdfFiles.Max(file => file.LastWriteTimeUtc)
            : (DateTime?)null;

        foreach (var child in children)
        {
            if (child.LastModifiedUtc is { } childLast && (lastModified is null || childLast > lastModified))
            {
                lastModified = childLast;
            }
        }

        var status = !string.IsNullOrEmpty(childEnumerationError)
            ? childEnumerationError!
            : BuildBranchStatusMessage(lastModified, totalPdfCount, children.Count);

        return new BranchEditStatus(
            directory.Name,
            segments.ToList(),
            pdfCount,
            totalPdfCount,
            lastModified,
            status,
            recentFiles,
            documentStatuses,
            children,
            childEnumerationError);
    }

    private static string BuildBranchStatusMessage(DateTime? lastModifiedUtc, int totalPdfCount, int childCount)
    {
        if (totalPdfCount == 0)
        {
            return childCount > 0
                ? "ยังไม่มีไฟล์ PDF ในกิ่งนี้"
                : "ยังไม่มีไฟล์ PDF";
        }

        if (lastModifiedUtc is null)
        {
            return "มีไฟล์ PDF แต่ไม่พบข้อมูลการอัปเดต";
        }

        return $"อัปเดตล่าสุด {DescribeRelativeTime(lastModifiedUtc.Value)}";
    }

    private LineEditStatus BuildLineEditStatus(string line)
    {
        var lineDirectory = Path.Combine(_pdfRoot, line);

        if (!Directory.Exists(lineDirectory))
        {
            return new LineEditStatus(line, null, "ไม่พบโฟลเดอร์สำหรับไลน์นี้บนเซิร์ฟเวอร์");
        }

        try
        {
            var directoryInfo = new DirectoryInfo(lineDirectory);
            var rootBranch = BuildBranchStatus(directoryInfo, Array.Empty<string>());
            return new LineEditStatus(line, rootBranch, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LineEditStatus(line, null, $"ไม่สามารถอ่านข้อมูลได้: {ex.Message}");
        }
    }
}