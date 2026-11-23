using System;
using System.IO;

namespace gamevault.Helper
{
    /// <summary>The type of a file sent to the server: only images, save zips and database files are ever sent.</summary>
    internal static class MimeTypeHelper
    {
        public static string GetMimeType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".zip" => "application/zip",
            _ => "application/octet-stream",
        };
    }
}
