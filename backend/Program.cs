using VirtualInfluencer.Backend.Configuration;
using VirtualInfluencer.Backend.Contracts;
using VirtualInfluencer.Backend.Services;

EnvFileLoader.LoadIfPresent(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);
var openAiOptions = OpenAiOptions.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(openAiOptions);
builder.Services.AddHttpClient<OpenAiApiClient>();
builder.Services.AddSingleton<VoiceGateway>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
});

app.MapGet("/health", () =>
{
    var response = new HealthResponse(
        Status: "ok",
        TimestampUtc: DateTimeOffset.UtcNow,
        OpenAiKeyConfigured: openAiOptions.HasApiKey,
        DotNetRuntime: Environment.Version.ToString());

    return Results.Ok(response);
});

app.MapPost("/session/realtime", async (RealtimeSessionRequest request, OpenAiApiClient client, CancellationToken cancellationToken) =>
{
    if (!openAiOptions.HasApiKey)
    {
        return Results.Problem(
            title: "OPENAI_API_KEY fehlt",
            detail: "Lege OPENAI_API_KEY in backend/.env oder als Umgebungsvariable fest.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        var session = await client.CreateRealtimeClientSecretAsync(request, cancellationToken);
        return Results.Ok(session);
    }
    catch (OpenAiApiException exception)
    {
        return Results.Problem(
            title: "Realtime Session konnte nicht erstellt werden",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Map("/voice/realtime", async context =>
{
    var gateway = context.RequestServices.GetRequiredService<VoiceGateway>();
    await gateway.HandleAsync(context, VoicePipelineMode.Realtime);
});

app.Map("/voice/modular", async context =>
{
    var gateway = context.RequestServices.GetRequiredService<VoiceGateway>();
    await gateway.HandleAsync(context, VoicePipelineMode.Modular);
});

app.Run();
