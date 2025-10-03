var builder = WebApplication.CreateBuilder(args);

// เปิดโหมด Blazor Server
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

// ====== PDF APIs ======
var pdfRoot = app.Configuration["PdfStorage:Root"]
              ?? Path.Combine(app.Environment.ContentRootPath, "PDFs");
Directory.CreateDirectory(pdfRoot);

// กัน path traversal + บังคับ .pdf
bool IsSafeFileName(string name)
{
    if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
    {
        return false;
    }

    return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}

// 1) รายการไฟล์
app.MapGet("/api/pdfs", () =>
{
    var files = Directory.EnumerateFiles(pdfRoot, "*.pdf", SearchOption.TopDirectoryOnly)
                         .Select(full => new
                         {
                             name = Path.GetFileName(full),
                             sizeBytes = new FileInfo(full).Length,
                             modifiedUtc = File.GetLastWriteTimeUtc(full),
                         })
                         .OrderByDescending(f => f.modifiedUtc);
    return Results.Ok(files);
});

IResult? ValidatePdfRequest(string name, out string fullPath)
{
    fullPath = string.Empty;
    if (!IsSafeFileName(name))
    {
        return Results.BadRequest("invalid file name");
    }

    var candidate = Path.Combine(pdfRoot, name);
    if (!System.IO.File.Exists(candidate))
    {
        return Results.NotFound();
    }

    fullPath = candidate;
    return null;
}

// 2) พรีวิว inline
app.MapGet("/api/pdfs/{name}", (string name) =>
{
    if (ValidatePdfRequest(name, out var fullPath) is { } error)
    {
        return error;
    }

    return Results.File(fullPath, "application/pdf", enableRangeProcessing: true);
});

// 3) ดาวน์โหลด (บังคับแนบไฟล์)
app.MapGet("/api/pdfs/{name}/download", (string name) =>
{
    if (ValidatePdfRequest(name, out var fullPath) is { } error)
    {
        return error;
    }

    var stream = System.IO.File.OpenRead(fullPath);
    return Results.File(stream, "application/pdf", fileDownloadName: name, enableRangeProcessing: true);
});
// ====== /PDF APIs ======

// NOTE: เปลี่ยน BlazorPdfApp เป็นชื่อ namespace โปรเจกต์คุณถ้าไม่ตรง
app.MapRazorComponents<BlazorPdfApp.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
