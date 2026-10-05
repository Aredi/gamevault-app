using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Line based "key=value" settings file. Source replacement for the former binary Lib/Preferences.dll,
/// reading and writing the same format so existing user configs keep working.
///
/// Encrypted values use DPAPI on Windows. Other platforms have no DPAPI, so values are stored as
/// plain text and the file is restricted to the current user (chmod 600) instead.
/// </summary>
public class Preferences
{
    private static readonly object fileLock = new object();

    public static T? Get<T>(string key, string file, bool useEncryption = false)
    {
        string text = Get(key, file, useEncryption);
        return text != string.Empty ? JsonSerializer.Deserialize<T>(text) : default;
    }

    public static T? Get<T>(Enum key, string file, bool useEncryption = false) => Get<T>(key.ToString(), file, useEncryption);

    public static string Get(Enum key, string file, bool useEncryption = false) => Get(key.ToString(), file, useEncryption);

    public static string Get(string key, string file, bool useEncryption = false)
    {
        lock (fileLock)
        {
            PrepareFile(file, useEncryption);
            foreach (string line in File.ReadLines(file))
            {
                if (IsKeyLine(line, key))
                {
                    string value = line.Substring(key.Length + 1);
                    return useEncryption ? Unprotect(value) : value;
                }
            }
        }
        return string.Empty;
    }

    public static void Set(string key, object value, string file, bool useEncryption = false) => Set(key, JsonSerializer.Serialize(value), file, useEncryption);

    public static void Set(Enum key, object value, string file, bool useEncryption = false) => Set(key.ToString(), value, file, useEncryption);

    public static void Set(Enum key, string value, string file, bool useEncryption = false) => Set(key.ToString(), value, file, useEncryption);

    public static void Set(string key, string value, string file, bool useEncryption = false)
    {
        if (useEncryption)
            value = Protect(value);

        lock (fileLock)
        {
            PrepareFile(file, useEncryption);
            var lines = new List<string>(File.ReadAllLines(file));
            int index = lines.FindIndex(l => IsKeyLine(l, key));
            if (index >= 0)
                lines[index] = $"{key}={value}";
            else
                lines.Add($"{key}={value}");
            File.WriteAllLines(file, lines);
        }
    }

    public static void DeleteKey(Enum key, string file) => DeleteKey(key.ToString(), file);

    public static void DeleteKey(string key, string file)
    {
        lock (fileLock)
        {
            PrepareFile(file, false);
            var lines = new List<string>(File.ReadAllLines(file));
            if (lines.RemoveAll(l => IsKeyLine(l, key)) > 0)
                File.WriteAllLines(file, lines);
        }
    }

    public static bool Exists(string key, string file)
    {
        lock (fileLock)
        {
            PrepareFile(file, false);
            return File.ReadLines(file).Any(l => IsKeyLine(l, key));
        }
    }

    // The original implementation matched on the key prefix only, so "Theme" also matched "ThemeX=...".
    private static bool IsKeyLine(string? line, string key)
    {
        return line != null && line.Length > key.Length && line[key.Length] == '=' && line.StartsWith(key, StringComparison.Ordinal);
    }

    private static string Protect(string value)
    {
        if (!OperatingSystem.IsWindows())
            return value;
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    private static string Unprotect(string value)
    {
        if (!OperatingSystem.IsWindows())
            return value;
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    }

    private static void PrepareFile(string file, bool holdsSecrets)
    {
        if (!File.Exists(file))
        {
            string? dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Create(file).Close();
        }
        if (holdsSecrets && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
