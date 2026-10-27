using GameVault.Uploader;

var builder = WebApplication.CreateSlimBuilder(args);
// Archives of several GB arrive in chunks; one chunk may still be big
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = UploadStore.MaxChunkSize + 1024);
var options = UploaderOptions.From(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<UploadStore>();
builder.Services.AddSingleton<ProfileStore>();
builder.Services.AddSingleton<SaveStore>();
builder.Services.AddSingleton<RemoteInstallStore>();
builder.Services.AddSingleton<RetroSaveStore>();
builder.Services.AddSingleton<RetroPlayerStore>();
builder.Services.AddSingleton<RoomStore>();
builder.Services.AddSingleton<HiddenPlayStore>();
builder.Services.AddSingleton<ReportStore>();
builder.Services.AddHttpClient<AdminCheck>(client =>
{
    client.BaseAddress = new Uri(options.GameVaultUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<GameVaultProxy>(client =>
{
    client.BaseAddress = new Uri(options.GameVaultUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<GameRequestsProxy>(client =>
{
    if (!string.IsNullOrEmpty(options.RommCommUrl))
        client.BaseAddress = new Uri(options.RommCommUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<ZeroTierController>(client =>
{
    if (!string.IsNullOrEmpty(options.ZeroTierControllerUrl))
        client.BaseAddress = new Uri(options.ZeroTierControllerUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<PcGamingWiki>(client =>
{
    client.BaseAddress = new Uri("https://www.pcgamingwiki.com/w/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"SanctuaryVault/{UploaderOptions.Version} (+https://github.com/Aredi/gamevault-app)");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<RommProxy>(client =>
{
    if (!string.IsNullOrEmpty(options.RommUrl))
        client.BaseAddress = new Uri(options.RommUrl.TrimEnd('/') + "/");
    // Games of several GB come through: the time goes to each read, not to the whole download
    client.Timeout = Timeout.InfiniteTimeSpan;
});

var app = builder.Build();
// Also served under the GameVault site (gamevault.example.com/sanctuary/ -> this service): same origin as the
// GameVault web page, whose sign-in the page then uses. The page's links are relative, so it needs the final slash.
app.UsePathBase("/sanctuary");
app.Use(async (context, next) =>
{
    // The page signs in with the GameVault account: no other site may show it in a frame (to make a player click)
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.PathBase.HasValue && !context.Request.Path.HasValue)
    {
        context.Response.Redirect(context.Request.PathBase + "/" + context.Request.QueryString);
        return;
    }
    await next();
});
// The web page (wwwroot): install a game on my PC, publish a game from a browser
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapUploadEndpoints();
app.Logger.LogInformation("GameVault Uploader {Version}: files in {Files}, profiles in {Profiles}, users checked by {Server}", UploaderOptions.Version, options.FilesDirectory, options.ProfilesDirectory, options.GameVaultUrl);
_ = app.Services.GetRequiredService<ProfileStore>();// says at once whether profiles are kept
_ = app.Services.GetRequiredService<SaveStore>();
_ = app.Services.GetRequiredService<RemoteInstallStore>();
_ = app.Services.GetRequiredService<RetroSaveStore>();
_ = app.Services.GetRequiredService<RetroPlayerStore>();
_ = app.Services.GetRequiredService<RoomStore>();
_ = app.Services.GetRequiredService<ReportStore>();
app.Run();

public partial class Program { }
