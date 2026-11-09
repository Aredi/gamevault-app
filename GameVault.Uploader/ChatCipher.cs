using System.Security.Cryptography;
using System.Text;

namespace GameVault.Uploader
{
    /// <summary>
    /// What the chat writes on the disk, sealed with AES-256-GCM: a copy of the folder (a backup, a stolen disk) can
    /// not be read without the key, and a changed byte is seen. Each piece: its nonce (12 bytes), its tag (16), the text.
    /// The key is CHAT_KEY (32 bytes in base64), or a file beside the chat's folder, made once.
    /// </summary>
    public sealed class ChatCipher
    {
        private const int NonceSize = 12, TagSize = 16;
        private readonly byte[] key;

        public ChatCipher(byte[] key)
        {
            if (key.Length != 32)
                throw new ArgumentException("The chat's key is 32 bytes (CHAT_KEY: 32 bytes in base64).");
            this.key = key;
        }

        /// <summary>CHAT_KEY when it is given, otherwise the key file (made the first time, readable by the service only).</summary>
        public static ChatCipher Load(string? configured, string keyFile)
        {
            if (!string.IsNullOrWhiteSpace(configured))
                return new ChatCipher(Convert.FromBase64String(configured.Trim()));
            if (!File.Exists(keyFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
                string part = keyFile + ".part";
                File.WriteAllText(part, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(part, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                // Never over a key made meanwhile: what it sealed would be lost
                try { File.Move(part, keyFile); }
                catch (IOException) when (File.Exists(keyFile)) { File.Delete(part); }
            }
            return new ChatCipher(Convert.FromBase64String(File.ReadAllText(keyFile).Trim()));
        }

        public byte[] Seal(ReadOnlySpan<byte> plain)
        {
            var sealedData = new byte[NonceSize + TagSize + plain.Length];
            Span<byte> nonce = sealedData.AsSpan(0, NonceSize), tag = sealedData.AsSpan(NonceSize, TagSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain, sealedData.AsSpan(NonceSize + TagSize), tag);
            return sealedData;
        }

        /// <exception cref="CryptographicException">Another key, or changed bytes.</exception>
        public byte[] Open(ReadOnlySpan<byte> sealedData)
        {
            if (sealedData.Length < NonceSize + TagSize)
                throw new CryptographicException("Too short to be sealed.");
            var plain = new byte[sealedData.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(sealedData[..NonceSize], sealedData[(NonceSize + TagSize)..], sealedData.Slice(NonceSize, TagSize), plain);
            return plain;
        }

        /// <summary>A line of the messages' files: base64, never a line break inside.</summary>
        public string SealText(string plain) => Convert.ToBase64String(Seal(Encoding.UTF8.GetBytes(plain)));

        public string OpenText(string sealedText) => Encoding.UTF8.GetString(Open(Convert.FromBase64String(sealedText)));
    }
}
