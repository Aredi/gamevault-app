namespace GameVault.Core.Tests
{
    public class PreferencesTests : IDisposable
    {
        private readonly string file = Path.Combine(Path.GetTempPath(), $"gv-prefs-{Guid.NewGuid()}", "config");

        public void Dispose()
        {
            Directory.Delete(Path.GetDirectoryName(file)!, true);
        }

        [Fact]
        public void SetThenGet_RoundTrips()
        {
            Preferences.Set("ServerUrl", "https://demo.gamevau.lt", file);
            Assert.Equal("https://demo.gamevau.lt", Preferences.Get("ServerUrl", file));
            Assert.True(Preferences.Exists("ServerUrl", file));
        }

        [Fact]
        public void Get_MissingKey_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, Preferences.Get("Nope", file));
        }

        [Fact]
        public void Set_DoesNotOverwriteKeysSharingAPrefix()
        {
            Preferences.Set("ThemeColor", "red", file);
            Preferences.Set("Theme", "dark", file);

            Assert.Equal("red", Preferences.Get("ThemeColor", file));
            Assert.Equal("dark", Preferences.Get("Theme", file));
        }

        [Fact]
        public void Set_ExistingKey_ReplacesValue()
        {
            Preferences.Set("Theme", "dark", file);
            Preferences.Set("Theme", "light", file);
            Assert.Equal("light", Preferences.Get("Theme", file));
            Assert.Single(File.ReadAllLines(file));
        }

        [Fact]
        public void Values_MayContainEqualsSigns()
        {
            Preferences.Set("Token", "abc==", file);
            Assert.Equal("abc==", Preferences.Get("Token", file));
        }

        [Fact]
        public void DeleteKey_RemovesOnlyThatKey()
        {
            Preferences.Set("Theme", "dark", file);
            Preferences.Set("ThemeColor", "red", file);
            Preferences.DeleteKey("Theme", file);

            Assert.False(Preferences.Exists("Theme", file));
            Assert.Equal("red", Preferences.Get("ThemeColor", file));
        }

        [Fact]
        public void GenericGetSet_UsesJson()
        {
            Preferences.Set("Ids", new[] { 1, 2, 3 }, file);
            Assert.Equal(new[] { 1, 2, 3 }, Preferences.Get<int[]>("Ids", file));
        }

        [Fact]
        public void EncryptedValues_RoundTrip_AndRestrictFileOnUnix()
        {
            Preferences.Set("SessionToken", "secret", file, true);
            Assert.Equal("secret", Preferences.Get("SessionToken", file, true));

            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            }
            else
            {
                Assert.DoesNotContain("secret", File.ReadAllText(file));
            }
        }

        [Fact]
        public void LineBreaksInAValue_DoNotAddLines()
        {
            Preferences.Set("Notes", "first\nsecond=x", file);
            Assert.Equal("first second=x", Preferences.Get("Notes", file));
            Assert.Single(File.ReadAllLines(file));
        }

        [Fact]
        public void Rewrites_KeepTheRestrictedPermissions()
        {
            Preferences.Set("SessionToken", "secret", file, true);
            Preferences.Set("Theme", "dark", file);
            Preferences.DeleteKey("Theme", file);
            Assert.False(File.Exists(file + ".tmp"));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
    }
}
