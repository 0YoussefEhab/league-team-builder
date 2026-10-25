using SkiaSharp;

namespace LeagueTeamBuilder.Services
{
    // Local-disk version of HFECloud's FileUploadService (R2 is dropped). Same method names and shapes, so the League code is unchanged.
    // A file is saved under  <webRoot>/<folder>/<random name><extension>  and its URL is  /<folder>/<random name><extension>.
    // The name is random (never the uploaded one), and delete only touches files under <webRoot>/uploads/.
    public static class FileUploadService
    {
        private const string UploadsRoot = "uploads/";

        // Null when the file is acceptable, otherwise the sentence to show. Extensions are lower-case with the dot (".png").
        public static string? ValidatePublicFile(IFormFile? file, string label, string[] allowedExtensions, long maxBytes)
        {
            if (file is null || file.Length == 0) return $"{label}: choose a file first.";
            if (file.Length > maxBytes) return $"{label} is too big. The limit is {SizeText(maxBytes)}.";
            var ext = ExtensionOf(file);
            if (!allowedExtensions.Contains(ext)) return $"{label} must be one of: {string.Join(", ", allowedExtensions)}.";
            return null;
        }

        // Validate, check that it really decodes as an image, then save. (Url, SavedName, Error): Error is set when nothing was saved.
        public static async Task<(string? Url, string? SavedName, string? Error)> SaveValidatedAsync(
            IFormFile file, string webRootPath, string folder, string[] allowedExtensions, long maxBytes)
        {
            var problem = ValidatePublicFile(file, "The picture", allowedExtensions, maxBytes);
            if (problem is not null) return (null, null, problem);

            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                using var data = SKData.CreateCopy(ms.ToArray());
                using var codec = SKCodec.Create(data);
                if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                    return (null, null, "That file isn't a readable image.");
            }

            var url = await SaveAsync(file, webRootPath, folder);
            if (url is null) return (null, null, "The upload failed. Try again.");
            return (url, Path.GetFileName(url), null);
        }

        // Saves without checking anything (call ValidatePublicFile first). Null when the disk write failed.
        public static async Task<string?> SaveAsync(IFormFile file, string webRootPath, string folder)
        {
            try
            {
                var cleanFolder = folder.Trim('/', '\\');
                var dir = Path.Combine(webRootPath, cleanFolder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(dir);

                var name = Guid.NewGuid().ToString("N") + ExtensionOf(file);
                await using (var fs = new FileStream(Path.Combine(dir, name), FileMode.CreateNew, FileAccess.Write))
                    await file.CopyToAsync(fs);
                return "/" + cleanFolder + "/" + name;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // Deletes the file behind an upload URL if there is one. Never throws.
        public static void DeleteIfExists(string? url, string webRootPath)
        {
            var path = LocalPathOf(url, webRootPath);
            if (path is null) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // After a replace or a remove: deletes the OLD file, but not when it is the file that was just saved.
        public static void DeleteReplacedUpload(string? oldUrl, string? newUrl, string webRootPath)
        {
            if (string.IsNullOrEmpty(oldUrl) || string.Equals(oldUrl, newUrl, StringComparison.Ordinal)) return;
            DeleteIfExists(oldUrl, webRootPath);
        }

        // The disk path of a "/uploads/..." URL, or null for anything else (an outside URL, a path that climbs out of the folder).
        private static string? LocalPathOf(string? url, string webRootPath)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var rel = url.Trim().TrimStart('/');
            if (!rel.StartsWith(UploadsRoot, StringComparison.OrdinalIgnoreCase) || rel.Contains("..")) return null;

            var root = Path.GetFullPath(webRootPath);
            var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        private static string ExtensionOf(IFormFile file) =>
            (Path.GetExtension(file.FileName) ?? string.Empty).ToLowerInvariant();

        private static string SizeText(long bytes) =>
            bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024d):0.#} MB" : $"{bytes / 1024d:0} KB";
    }
}
