using System.ClientModel;
using System.Threading.RateLimiting;
using Dapper;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using Pgvector.Dapper;
using RagKb.Api.Data;
using RagKb.Api.Infrastructure;
using RagKb.Api.Options;
using RagKb.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Render/Railway inject PORT; locally we default to 5080.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ---------- Options (validated at startup — fail fast on a missing API key) ----------
builder.Services.AddOptions<AiOptions>()
    .BindConfiguration(AiOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RagOptions>()
    .BindConfiguration(RagOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(o => o.ChunkOverlap * 2 <= o.ChunkSize, "Rag:ChunkOverlap must be at most half of Rag:ChunkSize.")
    .ValidateOnStart();

builder.Services.AddOptions<SecurityOptions>()
    .BindConfiguration(SecurityOptions.SectionName);

var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = (ragOptions.MaxUploadSizeMb + 1) * 1024L * 1024L);

// ---------- PostgreSQL + pgvector ----------
SqlMapper.AddTypeHandler(new VectorTypeHandler());

builder.Services.AddSingleton(_ =>
{
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionStringResolver.Resolve(builder.Configuration));
    dataSourceBuilder.UseVector();
    return dataSourceBuilder.Build();
});
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddSingleton<IDocumentRepository, DocumentRepository>();

// ---------- AI provider: Gemini (free tier) or OpenAI, both via the OpenAI SDK ----------
// Clients are thread-safe and meant to be singletons.
static OpenAIClientOptions ClientOptions(AiOptions o) => new() { Endpoint = new Uri(o.ResolvedEndpoint) };

builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    return new EmbeddingClient(o.ResolvedEmbeddingModel, new ApiKeyCredential(o.ApiKey), ClientOptions(o));
});
builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    return new ChatClient(o.ResolvedChatModel, new ApiKeyCredential(o.ApiKey), ClientOptions(o));
});
builder.Services.AddSingleton<AiService>();
builder.Services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<AiService>());
builder.Services.AddSingleton<IChatService>(sp => sp.GetRequiredService<AiService>());

// ---------- RAG pipeline ----------
builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<ITextChunker, TextChunker>();
builder.Services.AddScoped<IRagService, RagService>();

// ---------- Web ----------
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var allowedOrigins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .SetIsOriginAllowedToAllowWildcardSubdomains() // allows entries like https://*.vercel.app
    .WithMethods("GET", "POST", "DELETE")
    .WithHeaders("Content-Type", ApiKeyMiddleware.HeaderName)));

// Per-IP limits protect your OpenAI bill from a leaked demo URL.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitPolicies.Upload, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10) }));
    o.AddPolicy(RateLimitPolicies.Chat, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

// Behind Render/Railway proxies: trust X-Forwarded-* so rate limiting sees real client IPs.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // GET /openapi/v1.json
}

app.UseDefaultFiles();
app.UseStaticFiles(); // serves wwwroot (React build) when present

app.UseCors();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseRateLimiter();

app.MapControllers();

app.MapGet("/health", async (NpgsqlDataSource db, CancellationToken ct) =>
{
    try
    {
        await using var cmd = db.CreateCommand("SELECT 1");
        await cmd.ExecuteScalarAsync(ct);
        return Results.Ok(new { status = "healthy", database = "up" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "unhealthy", database = ex.GetType().Name }, statusCode: 503);
    }
});

// In the Docker image the built React app sits in wwwroot, so UI + API share one URL.
var hasUi = File.Exists(Path.Combine(app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "index.html"));
if (hasUi)
{
    app.MapFallbackToFile("index.html"); // any non-API path -> SPA
}
else
{
    app.MapGet("/", () => Results.Redirect("/health")); // local dev: UI runs on Vite (:5173)
}

app.Run();

public partial class Program; // for WebApplicationFactory in integration tests
