namespace GameVault.Core.Downloads
{
    /// <summary>Tells a damaged archive (worth downloading again) from other extraction failures (full disk, wrong password).</summary>
    public static class ArchiveErrors
    {
        private static readonly string[] DamageMarkers =
        {
            // 7-Zip
            "CRC Failed", "Data Error", "Headers Error", "Unexpected end of archive", "Can not open the file as archive",
            "There are some data after the end of the payload data", "Unconfirmed start of archive", "Is not archive",
            // tar / gzip / xz
            "unexpected end of file", "invalid compressed data", "Unexpected EOF in archive", "not in gzip format",
            "Compressed data is corrupt", "File format not recognized", "Skipping to next header", "crc error",
        };

        private static readonly string[] OtherCauses =
        {
            "Wrong password", "Permission denied", "Access is denied",
        };

        // 7-Zip prints the system's message, in the language of Windows
        private static readonly string[] DiskFullMarkers =
        {
            "No space left on device", "There is not enough space on the disk", "Espace insuffisant sur le disque",
        };

        public static bool IsDiskFull(string? output) =>
            !string.IsNullOrEmpty(output) && DiskFullMarkers.Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase));

        public static bool IsDamaged(string? output)
        {
            if (string.IsNullOrWhiteSpace(output) || IsDiskFull(output) || OtherCauses.Any(cause => output.Contains(cause, StringComparison.OrdinalIgnoreCase)))
                return false;
            return DamageMarkers.Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }
}
