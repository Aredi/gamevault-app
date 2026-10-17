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

var app = builder.Build();
// Also served under the GameVault site (gamevault.example.com/sanctuary/ -> this service): same origin as the
// GameVault web page, whose sign-in the page then uses. The page's links are relative, so it needs the final slash.
app.UsePathBase("/sanctuary");
app.Use(async (context, next) =>
{
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
app.Run();

public partial class Program { }
