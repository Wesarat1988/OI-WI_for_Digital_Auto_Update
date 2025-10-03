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

// 2) พรีวิว/ดาวน์โหลด
app.MapGet("/api/pdfs/{name}", (string name) =>
{
    if (!IsSafeFileName(name)) return Results.BadRequest("invalid file name");
    var fullPath = Path.Combine(pdfRoot, name);
    if (!System.IO.File.Exists(fullPath)) return Results.NotFound();

    var stream = System.IO.File.OpenRead(fullPath);
    return Results.File(stream, "application/pdf", fileDownloadName: name, enableRangeProcessing: true);
});
// ====== /PDF APIs ======

// NOTE: เปลี่ยน BlazorPdfApp เป็นชื่อ namespace โปรเจกต์คุณถ้าไม่ตรง
app.MapRazorComponents<BlazorPdfApp.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
