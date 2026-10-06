using GameVault.Uploader;

var builder = WebApplication.CreateSlimBuilder(args);
// Archives of several GB arrive in chunks; one chunk may still be big
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = UploadStore.MaxChunkSize + 1024);
var options = UploaderOptions.From(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<UploadStore>();
builder.Services.AddHttpClient<AdminCheck>(client =>
{
    client.BaseAddress = new Uri(options.GameVaultUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();
app.MapUploadEndpoints();
app.Logger.LogInformation("GameVault Uploader {Version}: files in {Files}, users checked by {Server}", UploaderOptions.Version, options.FilesDirectory, options.GameVaultUrl);
app.Run();

public partial class Program { }
