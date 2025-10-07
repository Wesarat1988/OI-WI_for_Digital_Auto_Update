using System.Net.Mime;
using Microsoft.AspNetCore.Http.Features;

const long MaxUploadBytes = 50L * 1024 * 1024; // 50 MB upload limit per PDF

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes;
});

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
var allowedLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "F1",
    "F2",
    "F3"
};

static bool IsValidPdfFileName(string fileName)
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

static bool IsValidFolderName(string folderName)
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

static IReadOnlyList<string> ParsePathSegments(string? rawPath)
{
    if (string.IsNullOrWhiteSpace(rawPath))
    {
        return Array.Empty<string>();
    }

    return rawPath
        .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

bool TryResolveLine(string line, out string? linePath)
{
    linePath = null;
    if (!allowedLines.Contains(line))
    {
        return false;
    }

    var candidate = Path.Combine(pdfRoot, line);
    if (!Directory.Exists(candidate))
    {
        return false;
    }

    linePath = candidate;
    return true;
}

bool TryResolveDirectory(string line, IReadOnlyList<string> segments, out string? directoryPath, out List<string>? normalizedSegments, out IResult? error)
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

IResult? TryResolvePdf(string line, IReadOnlyList<string> pathSegments, string file, out string? filePath)
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
    if (!System.IO.File.Exists(candidate))
    {
        return Results.NotFound();
    }

    filePath = candidate;
    return null;
}

static string BuildRelativeFilePath(IReadOnlyList<string> segments, string fileName)
{
    if (segments.Count == 0)
    {
        return fileName;
    }

    return string.Join('/', segments) + "/" + fileName;
}

static string DescribeRelativeTime(DateTime timestampUtc)
{
    var now = DateTime.UtcNow;
    var delta = now - timestampUtc;

    if (delta < TimeSpan.Zero)
    {
        delta = TimeSpan.Zero;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        app.Logger.LogError(ex, "Failed to enumerate folders under {PdfRoot}", pdfRoot);
        return Results.Problem("ไม่สามารถอ่านรายการโฟลเดอร์ได้ กรุณาตรวจสอบการแชร์และสิทธิ์การเข้าถึง");
    }
});

app.MapGet("/api/folders/{line}", (string line, HttpRequest request) =>
{
    var pathSegments = ParsePathSegments(request.Query["path"]);
    if (!TryResolveDirectory(line, pathSegments, out var directoryPath, out var normalizedSegments, out var error))
    {
        return error ?? Results.NotFound();
    }

    try
    {
        var folders = Directory.EnumerateDirectories(directoryPath!, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && IsValidFolderName(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => name!)
            .ToList();

        var files = Directory.EnumerateFiles(directoryPath!, "*.pdf", SearchOption.TopDirectoryOnly)
            .Where(path => IsValidPdfFileName(Path.GetFileName(path)))
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => name!)
            .ToList();

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

static string BuildBranchStatusMessage(DateTime? lastModifiedUtc, int totalPdfCount, int childCount)
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

BranchEditStatus BuildBranchStatus(DirectoryInfo directory, IReadOnlyList<string> segments)
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
            Children: Array.Empty<BranchEditStatus>(),
            ErrorMessage: $"ไม่สามารถอ่านไฟล์: {ex.Message}");
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
        children,
        childEnumerationError);
}

LineEditStatus BuildLineEditStatus(string line, string pdfRootPath)
{
    var lineDirectory = Path.Combine(pdfRootPath, line);

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

app.MapGet("/api/folders", () =>
{
    try
    {
        var existing = allowedLines
            .Select(line => new { line, path = Path.Combine(pdfRoot, line) })
            .Where(x => Directory.Exists(x.path))
            .Select(x => x.line)
            .OrderBy(line => line)
            .ToList();

        return Results.Ok(existing);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        app.Logger.LogError(ex, "Failed to enumerate folders under {PdfRoot}", pdfRoot);
        return Results.Problem("ไม่สามารถอ่านรายการโฟลเดอร์ได้ กรุณาตรวจสอบการแชร์และสิทธิ์การเข้าถึง");
    }
});

app.MapGet("/api/folders/{line}", (string line, HttpRequest request) =>
{
    var pathSegments = ParsePathSegments(request.Query["path"]);
    if (!TryResolveDirectory(line, pathSegments, out var directoryPath, out var normalizedSegments, out var error))
    {
        return error ?? Results.NotFound();
    }

    try
    {
        var folders = Directory.EnumerateDirectories(directoryPath!, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && IsValidFolderName(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => name!)
            .ToList();

        var files = Directory.EnumerateFiles(directoryPath!, "*.pdf", SearchOption.TopDirectoryOnly)
            .Where(path => IsValidPdfFileName(Path.GetFileName(path)))
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => name!)
            .ToList();

        return Results.Ok(new FolderListing(line, normalizedSegments ?? new List<string>(), folders, files));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        app.Logger.LogError(ex, "Failed to enumerate files for {Line} in {PdfRoot}", line, pdfRoot);
        return Results.Problem("ไม่สามารถอ่านไฟล์ในโฟลเดอร์ที่เลือกได้ กรุณาตรวจสอบสิทธิ์การเข้าถึง");
    }
});

app.MapGet("/pdf/{line}/{file}", (string line, string file, HttpRequest request) =>
{
    var pathSegments = ParsePathSegments(request.Query["path"]);

    if (TryResolvePdf(line, pathSegments, file, out var filePath) is { } error)
    {
        return error;
    }

    try
    {
        var stream = System.IO.File.OpenRead(filePath!);
        return Results.File(stream, MediaTypeNames.Application.Pdf, enableRangeProcessing: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        app.Logger.LogError(ex, "Failed to open PDF {File} for line {Line}", file, line);
        return Results.Problem("ไม่สามารถเปิดไฟล์ PDF ได้ กรุณาตรวจสอบการแชร์และสิทธิ์การเข้าถึง");
    }
});

app.MapGet("/api/edit-status", () =>
{
    var statuses = allowedLines
        .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
        .Select(line => BuildLineEditStatus(line, pdfRoot))
        .ToList();

    return Results.Ok(statuses);
});

app.MapPost("/api/folders/{line}/upload", async (string line, HttpRequest request) =>
{
    var pathSegments = ParsePathSegments(request.Query["path"]);
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

        if (formFile.Length > MaxUploadBytes)
        {
            return Results.BadRequest($"ไฟล์มีขนาดเกิน {MaxUploadBytes / (1024 * 1024)} MB");
        }

        var destination = Path.Combine(directoryPath!, originalName);
        if (System.IO.File.Exists(destination))
        {
            return Results.Conflict("ไฟล์นี้มีอยู่แล้ว");
        }

        await using var readStream = formFile.OpenReadStream();
        await using var writeStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await readStream.CopyToAsync(writeStream);

        return Results.Ok(new { file = originalName, path = pathSegments });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        app.Logger.LogError(ex, "Failed to upload PDF to {Line}", line);
        return Results.Problem("ไม่สามารถอัปโหลดไฟล์ได้ กรุณาตรวจสอบสิทธิ์การเข้าถึง");
    }
});

app.MapPost("/api/folders/{line}/subfolders", async (string line, HttpContext context) =>
{
    var pathSegments = ParsePathSegments(context.Request.Query["path"]);
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
        app.Logger.LogWarning(ex, "Invalid folder creation payload for line {Line}", line);
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
        app.Logger.LogError(ex, "Failed to create subfolder {Folder} for line {Line}", name, line);
        return Results.Problem("ไม่สามารถสร้างโฟลเดอร์ได้ กรุณาตรวจสอบสิทธิ์การเข้าถึง");
    }
});
// ====== /PDF browser configuration ======

app.MapRazorComponents<BlazorPdfApp.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();

internal sealed record FolderListing(string Line, IReadOnlyList<string> PathSegments, IReadOnlyList<string> Folders, IReadOnlyList<string> Files);
internal sealed record CreateFolderRequest(string? Name);
internal sealed record LineEditStatus(string Line, BranchEditStatus? Root, string? ErrorMessage);
internal sealed record BranchEditStatus(string Name, IReadOnlyList<string> PathSegments, int PdfCount, int TotalPdfCount, DateTime? LastModifiedUtc, string Status, IReadOnlyList<FileEditStatus> RecentFiles, IReadOnlyList<BranchEditStatus> Children, string? ErrorMessage);
internal sealed record FileEditStatus(string FileName, DateTime LastModifiedUtc, long SizeBytes, string RelativePath);
