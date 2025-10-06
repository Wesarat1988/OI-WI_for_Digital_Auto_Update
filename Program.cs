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

IResult? TryResolvePdf(string line, string file, out string? filePath)
{
    filePath = null;
    if (!TryResolveLine(line, out var linePath))
    {
        return Results.NotFound("Unknown folder");
    }

    if (!IsValidPdfFileName(file))
    {
        return Results.BadRequest("Invalid PDF file name");
    }

    var candidate = Path.Combine(linePath!, file);
    if (!System.IO.File.Exists(candidate))
    {
        return Results.NotFound();
    }

    filePath = candidate;
    return null;
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

app.MapGet("/api/folders/{line}", (string line) =>
{
    if (!TryResolveLine(line, out var linePath))
    {
        return Results.NotFound();
    }

    try
    {
        var files = Directory.EnumerateFiles(linePath!, "*.pdf", SearchOption.TopDirectoryOnly)
            .Where(path => IsValidPdfFileName(Path.GetFileName(path)))
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .OrderBy(name => name)
            .ToList()!;

        return Results.Ok(files);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        app.Logger.LogError(ex, "Failed to enumerate files for {Line} in {PdfRoot}", line, pdfRoot);
        return Results.Problem("ไม่สามารถอ่านไฟล์ในโฟลเดอร์ที่เลือกได้ กรุณาตรวจสอบสิทธิ์การเข้าถึง");
    }
});

app.MapGet("/pdf/{line}/{file}", (string line, string file) =>
{
    if (TryResolvePdf(line, file, out var filePath) is { } error)
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

app.MapPost("/api/folders/{line}/upload", async (string line, HttpRequest request) =>
{
    if (!TryResolveLine(line, out var linePath))
    {
        return Results.NotFound("Unknown folder");
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

        var destination = Path.Combine(linePath!, originalName);
        if (System.IO.File.Exists(destination))
        {
            return Results.Conflict("ไฟล์นี้มีอยู่แล้ว");
        }

        await using var readStream = formFile.OpenReadStream();
        await using var writeStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await readStream.CopyToAsync(writeStream);

        return Results.Ok(new { file = originalName });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        app.Logger.LogError(ex, "Failed to upload PDF to {Line}", line);
        return Results.Problem("ไม่สามารถอัปโหลดไฟล์ได้ กรุณาตรวจสอบสิทธิ์การเข้าถึง");
    }
});
// ====== /PDF browser configuration ======

app.MapRazorComponents<BlazorPdfApp.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
