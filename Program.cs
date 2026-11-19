using QueueSystem.Components;
using QueueSystem.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
var databasePath = builder.Configuration["QueueDatabase:Path"];
builder.Services.AddSingleton(_ => new QueueService(Path.GetFullPath(
    string.IsNullOrWhiteSpace(databasePath) ? "data/queues.db" : databasePath,
    builder.Environment.ContentRootPath)));

var app = builder.Build();
_ = app.Services.GetRequiredService<QueueService>();

app.UseStaticFiles();
app.UseAntiforgery();

// ---------- Backend: REST API ----------
var api = app.MapGroup("/api/queues");

api.MapGet("/", (QueueService q, string? service, DateOnly? date) =>
    date is { } selectedDate ? q.GetByDate(selectedDate, service) : q.GetToday(service));

api.MapPost("/", (QueueService q, CreateRequest r) =>
    QueueService.ServiceTypes.Any(s => s.Code == r.ServiceCode)
        ? Results.Ok(q.Create(r.ServiceCode, r.CustomerName, r.Remark))
        : Results.BadRequest("ไม่พบประเภทบริการ"));

api.MapPost("/call-next/{service}", (QueueService q, string service, DateOnly? date) =>
    q.CallNext(service, date) is { } t ? Results.Ok(t) : Results.NotFound("ไม่มีคิวที่รอเรียก"));

api.MapPost("/{id:guid}/complete", (QueueService q, Guid id) =>
    q.Complete(id) ? Results.NoContent() : Results.BadRequest());

api.MapPost("/{id:guid}/cancel", (QueueService q, Guid id, string? remark) =>
    q.Cancel(id, remark) ? Results.NoContent() : Results.BadRequest());

// ---------- Frontend: Blazor ----------
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

record CreateRequest(string ServiceCode, string CustomerName, string? Remark);
