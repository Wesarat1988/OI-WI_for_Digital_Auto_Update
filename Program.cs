using System.Net.Mime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();

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
// TODO: Replace the UNC path below with the actual PDF root (e.g. @"D:\\PdfRoot")
//       Ensure the web process identity has READ access on both the share and NTFS ACLs.
var pdfRoot = builder.Configuration["PdfStorage:Root"]
              ?? @"\\10.192.132.91\PdfRoot";
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
    var existing = allowedLines
        .Select(line => new { line, path = Path.Combine(pdfRoot, line) })
        .Where(x => Directory.Exists(x.path))
        .Select(x => x.line)
        .OrderBy(line => line)
        .ToList();

    return Results.Ok(existing);
});

app.MapGet("/api/folders/{line}", (string line) =>
{
    if (!TryResolveLine(line, out var linePath))
    {
        return Results.NotFound();
    }

    var files = Directory.EnumerateFiles(linePath!, "*.pdf", SearchOption.TopDirectoryOnly)
        .Where(path => IsValidPdfFileName(Path.GetFileName(path)))
        .Select(Path.GetFileName)
        .Where(name => name is not null)
        .OrderBy(name => name)
        .ToList()!;

    return Results.Ok(files);
});

app.MapGet("/pdf/{line}/{file}", (string line, string file) =>
{
    if (TryResolvePdf(line, file, out var filePath) is { } error)
    {
        return error;
    }

    var stream = System.IO.File.OpenRead(filePath!);
    return Results.File(stream, MediaTypeNames.Application.Pdf, enableRangeProcessing: true);
});
// ====== /PDF browser configuration ======

app.MapRazorComponents<BlazorPdfApp.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
