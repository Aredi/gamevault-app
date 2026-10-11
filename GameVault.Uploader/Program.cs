using GameVault.Uploader;

var builder = WebApplication.CreateSlimBuilder(args);
// Archives of several GB arrive in chunks; one chunk may still be big
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = UploadStore.MaxChunkSize + 1024);
var options = UploaderOptions.From(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<UploadStore>();
builder.Services.AddSingleton<ProfileStore>();
builder.Services.AddSingleton<SaveStore>();
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

var app = builder.Build();
// The web page (wwwroot): publish a game from a browser, without the client
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapUploadEndpoints();
app.Logger.LogInformation("GameVault Uploader {Version}: files in {Files}, profiles in {Profiles}, users checked by {Server}", UploaderOptions.Version, options.FilesDirectory, options.ProfilesDirectory, options.GameVaultUrl);
_ = app.Services.GetRequiredService<ProfileStore>();// says at once whether profiles are kept
_ = app.Services.GetRequiredService<SaveStore>();
app.Run();

public partial class Program { }
