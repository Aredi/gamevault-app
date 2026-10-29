using System.Security.Cryptography;
using System.Text.Json;

namespace GameVault.Uploader
{
    /// <summary>
    /// The invitation codes an administrator gives, each named after who it is for: a new account is activated only with one,
    /// and the code then says who used it. They never expire; each is used once. One JSON file for all, written whole.
    /// </summary>
    public sealed class InviteStore
    {
        // No 0/O, 1/I: read aloud or typed from a message
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly string file;
        private readonly object writing = new();
        /// <summary>Codes being redeemed (the account activated meanwhile): not usable twice at once.</summary>
        private readonly HashSet<string> redeeming = new();

        public InviteStore(UploaderOptions options, ILogger<InviteStore> logger)
        {
            file = Path.Combine(options.InvitesDirectory, "invites.json");
            bool writable = ProfileStore.IsWritable(options.InvitesDirectory, out string error);
            Enabled = writable && !string.IsNullOrEmpty(options.GameVaultApiKey);
            if (!writable)
                logger.LogWarning("Invitation codes are off: {Directory} can not be written ({Error}). Mount a volume there to offer them.", options.InvitesDirectory, error);
            else if (!Enabled)
                logger.LogWarning("Invitation codes are off: GAMEVAULT_API_KEY (an administrator's API key) is not set, so new accounts can not be activated.");
        }

        public bool Enabled { get; }

        public sealed record Invite(string Code, string Name, DateTime CreatedAt, string CreatedBy, DateTime? UsedAt, int? UserId, string? Username);

        /// <summary>As typed: case, spaces and dashes do not matter.</summary>
        public static string Normalize(string? code) => new string((code ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        private static string NewCode()
        {
            // 12 characters of 32: 60 bits, beyond guessing
            string code = new(Enumerable.Range(0, 12).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
            return $"{code[..4]}-{code[4..8]}-{code[8..]}";
        }

        private List<Invite> Read() => Enabled && File.Exists(file) ? JsonSerializer.Deserialize<List<Invite>>(File.ReadAllText(file)) ?? new() : new();

        private void Write(List<Invite> invites)
        {
            // Written aside then moved: never half written
            string part = file + ".part";
            File.WriteAllText(part, JsonSerializer.Serialize(invites));
            File.Move(part, file, overwrite: true);
        }

        /// <summary>Newest first.</summary>
        public List<Invite> List()
        {
            lock (writing)
                return Read().OrderByDescending(i => i.CreatedAt).ToList();
        }

        public Invite Create(string name, string admin)
        {
            lock (writing)
            {
                List<Invite> invites = Read();
                var invite = new Invite(NewCode(), name, DateTime.UtcNow, admin, null, null, null);
                invites.Add(invite);
                Write(invites);
                return invite;
            }
        }

        /// <summary>Null when deleted; else why not: "missing" or "used" (a used code stays, it tells who came with it).</summary>
        public string? Delete(string code)
        {
            lock (writing)
            {
                List<Invite> invites = Read();
                int index = invites.FindIndex(i => Normalize(i.Code) == Normalize(code));
                if (index < 0)
                    return "missing";
                if (invites[index].UsedAt != null || redeeming.Contains(Normalize(code)))
                    return "used";
                invites.RemoveAt(index);
                Write(invites);
                return null;
            }
        }

        /// <summary>Holds an unused code while its account is activated; null when there is no such unused code.</summary>
        public Invite? Reserve(string code)
        {
            lock (writing)
            {
                Invite? invite = Read().FirstOrDefault(i => Normalize(i.Code) == Normalize(code));
                if (invite == null || invite.UsedAt != null || !redeeming.Add(Normalize(code)))
                    return null;
                return invite;
            }
        }

        /// <summary>The account was not activated: the code can be used again.</summary>
        public void Release(string code)
        {
            lock (writing)
                redeeming.Remove(Normalize(code));
        }

        /// <summary>The account is activated: the code is spent, and tells who used it.</summary>
        public void Use(string code, int userId, string username)
        {
            lock (writing)
            {
                List<Invite> invites = Read();
                int index = invites.FindIndex(i => Normalize(i.Code) == Normalize(code));
                if (index >= 0)
                {
                    invites[index] = invites[index] with { UsedAt = DateTime.UtcNow, UserId = userId, Username = username };
                    Write(invites);
                }
                redeeming.Remove(Normalize(code));
            }
        }
    }
}
