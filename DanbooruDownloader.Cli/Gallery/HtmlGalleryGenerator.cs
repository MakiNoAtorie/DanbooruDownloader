using System;
using System.IO;
using System.Linq;
using System.Text;

namespace DanbooruDownloader.Cli.Gallery
{
    /// <summary>
    /// Generates a plain, dependency-free index.html for a download folder - since the CLI
    /// has no GUI thumbnail grid like the original WinForms app, this gives a browsable view
    /// of what's been collected. Uses the downloaded images themselves as thumbnails (CSS
    /// object-fit) rather than generating separate thumbnail files.
    /// </summary>
    public static class HtmlGalleryGenerator
    {
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

        public static void Generate(string folderPath, string title)
        {
            if (!Directory.Exists(folderPath)) return;

            // Recursive: the default filename-format nests into a %provider%/ subfolder, so
            // a non-recursive scan of the top-level folder would find nothing.
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine($"<title>{System.Net.WebUtility.HtmlEncode(title)}</title>");
            sb.AppendLine("""
                <style>
                  body { font-family: -apple-system, sans-serif; background: #111; color: #eee; margin: 0; padding: 16px; }
                  h1 { font-size: 16px; font-weight: 600; color: #999; margin: 0 0 16px; }
                  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 8px; }
                  .item { position: relative; aspect-ratio: 1; overflow: hidden; border-radius: 6px; background: #222; }
                  .item img { width: 100%; height: 100%; object-fit: cover; display: block; }
                  .item a { display: block; width: 100%; height: 100%; }
                  .name { position: absolute; bottom: 0; left: 0; right: 0; padding: 4px 6px; font-size: 11px;
                          background: linear-gradient(transparent, rgba(0,0,0,.85)); color: #fff;
                          white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
                </style>
                """);
            sb.AppendLine("</head><body>");
            sb.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(title)} &middot; {files.Count} file(s) &middot; generated {DateTime.Now:yyyy-MM-dd HH:mm}</h1>");
            sb.AppendLine("<div class=\"grid\">");

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(folderPath, file).Replace('\\', '/');
                var encodedPath = string.Join("/", relativePath.Split('/').Select(Uri.EscapeDataString));
                var displayName = System.Net.WebUtility.HtmlEncode(Path.GetFileName(file));
                sb.AppendLine($"""<div class="item"><a href="{encodedPath}" target="_blank"><img src="{encodedPath}" loading="lazy"><div class="name">{displayName}</div></a></div>""");
            }

            sb.AppendLine("</div></body></html>");

            File.WriteAllText(Path.Combine(folderPath, "index.html"), sb.ToString());
        }
    }
}
