using Godot;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using GodotFileAccess = Godot.FileAccess;

namespace WebViewSharp
{
    /// <summary>
    /// 网页资源的自定义服务层（对标 godot_wry 的 protocols.rs）。
    ///
    /// 为什么不直接用 WebView2 的 SetVirtualHostNameToFolderMapping：
    /// 那个 API 映射的是**文件系统目录**，而 res:// 在导出后位于 .pck 里，
    /// GlobalizePath("res://") 只会返回 exe 旁边的真实目录 —— 导出后必然 404。
    /// 这里改成拦截 https://godot.local/* 与 https://godot-user.local/* 的请求，
    /// 用 Godot 的 FileAccess 读文件，因此 PCK 内、甚至运行时挂载的额外 .pck 都能读到。
    ///
    /// 行为对齐参考实现：按扩展名给 MIME、目录自动回退 index.html、
    /// 404 返回文本、支持 Range（视频拖进度条需要 206）。
    /// </summary>
    internal static class WebResourceServer
    {
        internal const string ResHost = "godot.local";
        internal const string UserHost = "godot-user.local";

        /// <summary>注册拦截过滤器（必须在导航之前调用）。</summary>
        public static void Install(CoreWebView2 core)
        {
            core.AddWebResourceRequestedFilter($"https://{ResHost}/*", CoreWebView2WebResourceContext.All);
            core.AddWebResourceRequestedFilter($"https://{UserHost}/*", CoreWebView2WebResourceContext.All);
        }

        /// <summary>
        /// 命中我们自己的虚拟主机就返回响应，否则返回 null（交给 WebView2 正常网络流程）。
        /// </summary>
        public static CoreWebView2WebResourceResponse TryBuildResponse(
            CoreWebView2Environment environment,
            CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri))
                return null;

            string root;
            if (string.Equals(uri.Host, ResHost, StringComparison.OrdinalIgnoreCase))
                root = "res://";
            else if (string.Equals(uri.Host, UserHost, StringComparison.OrdinalIgnoreCase))
                root = "user://";
            else
                return null;

            string path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');

            // 目录穿越防护（浏览器会先规范化相对路径，这里再兜一层）
            if (Array.IndexOf(path.Split('/'), "..") >= 0)
                return Text(environment, 403, "Forbidden", "Path not allowed.");

            string requested = root + path;

            if (path.Length == 0 || path.EndsWith("/"))
            {
                string index = requested + "index.html";
                return GodotFileAccess.FileExists(index)
                    ? ServeFile(environment, index, GetRangeHeader(e))
                    : NotFound(environment, index);
            }

            if (GodotFileAccess.FileExists(requested))
                return ServeFile(environment, requested, GetRangeHeader(e));

            // 没有扩展名/是目录：回退到 index.html；没有尾斜杠时先重定向，保证相对路径正确
            string dirIndex = requested + "/index.html";
            if (GodotFileAccess.FileExists(dirIndex))
            {
                string location = $"{uri.Scheme}://{uri.Host}/{path}/";
                return environment.CreateWebResourceResponse(
                    Stream.Null, 302, "Found", $"Location: {location}\n");
            }

            return NotFound(environment, requested);
        }

        private static string GetRangeHeader(CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                return e.Request.Headers.GetHeader("Range");
            }
            catch
            {
                return null;
            }
        }

        private static CoreWebView2WebResourceResponse ServeFile(
            CoreWebView2Environment environment, string godotPath, string rangeHeader)
        {
            using var file = GodotFileAccess.Open(godotPath, GodotFileAccess.ModeFlags.Read);
            if (file == null)
                return Text(environment, 500, "Internal Server Error", $"Failed to open {godotPath}");

            long fileSize = (long)file.GetLength();
            string mime = MimeFor(godotPath);

            long start = 0;
            long end = fileSize - 1;
            bool partial = false;

            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                // 支持 "bytes=start-end" / "bytes=start-" / "bytes=-end"
                string spec = rangeHeader.Substring("bytes=".Length).Trim();
                string[] parts = spec.Split('-');
                partial = true;

                if (parts.Length == 2)
                {
                    if (parts[0].Length == 0 && long.TryParse(parts[1], out long tail) && tail > 0)
                    {
                        start = Math.Max(0, fileSize - tail);
                        end = fileSize - 1;
                    }
                    else
                    {
                        if (parts[0].Length > 0) long.TryParse(parts[0], out start);
                        if (parts[1].Length > 0 && long.TryParse(parts[1], out long explicitEnd)) end = explicitEnd;
                    }
                }

                if (start >= fileSize)
                {
                    return environment.CreateWebResourceResponse(
                        Stream.Null, 416, "Range Not Satisfiable",
                        $"Content-Type: {mime}\nAccept-Ranges: bytes\nContent-Range: bytes */{fileSize}\n");
                }

                if (end >= fileSize || end < start)
                    end = fileSize - 1;
            }

            int count = (int)Math.Max(0, end - start + 1);
            byte[] content = count > 0 ? ReadAt(file, start, count) : Array.Empty<byte>();

            string headers = $"Content-Type: {mime}\nAccept-Ranges: bytes\n";
            if (partial)
                headers += $"Content-Range: bytes {start}-{end}/{fileSize}\n";

            return environment.CreateWebResourceResponse(
                new MemoryStream(content, writable: false),
                partial ? 206 : 200,
                partial ? "Partial Content" : "OK",
                headers);
        }

        private static byte[] ReadAt(GodotFileAccess file, long offset, int count)
        {
            file.Seek((ulong)offset);
            return file.GetBuffer(count);
        }

        private static CoreWebView2WebResourceResponse NotFound(CoreWebView2Environment environment, string godotPath)
            => Text(environment, 404, "Not Found", $"Could not find file at {godotPath}");

        private static CoreWebView2WebResourceResponse Text(
            CoreWebView2Environment environment, int status, string reason, string body)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(body);
            return environment.CreateWebResourceResponse(
                new MemoryStream(bytes, writable: false), status, reason, "Content-Type: text/plain\n");
        }

        private static string MimeFor(string path)
        {
            int dot = path.LastIndexOf('.');
            if (dot < 0 || dot == path.Length - 1)
                return "application/octet-stream";

            string ext = path.Substring(dot + 1).ToLowerInvariant();
            return MimeTypes.TryGetValue(ext, out string mime) ? mime : "application/octet-stream";
        }

        // 与参考实现同一张表（https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/MIME_types/Common_types）
        private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["aac"] = "audio/aac", ["abw"] = "application/x-abiword", ["apng"] = "image/apng",
            ["arc"] = "application/x-freearc", ["avif"] = "image/avif", ["avi"] = "video/x-msvideo",
            ["azw"] = "application/vnd.amazon.ebook", ["bin"] = "application/octet-stream",
            ["bmp"] = "image/bmp", ["bz"] = "application/x-bzip", ["bz2"] = "application/x-bzip2",
            ["cda"] = "application/x-cdf", ["cjs"] = "text/javascript", ["csh"] = "application/x-csh",
            ["css"] = "text/css", ["csv"] = "text/csv", ["doc"] = "application/msword",
            ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ["eot"] = "application/vnd.ms-fontobject", ["epub"] = "application/epub+zip",
            ["gz"] = "application/gzip", ["gif"] = "image/gif", ["html"] = "text/html",
            ["htm"] = "text/html", ["ico"] = "image/vnd.microsoft.icon", ["ics"] = "text/calendar",
            ["jar"] = "application/java-archive", ["jpeg"] = "image/jpeg", ["jpg"] = "image/jpeg",
            ["js"] = "text/javascript", ["json"] = "application/json", ["jsonld"] = "application/ld+json",
            ["midi"] = "audio/midi", ["mid"] = "audio/midi", ["mjs"] = "text/javascript",
            ["mp3"] = "audio/mpeg", ["mp4"] = "video/mp4", ["mpeg"] = "video/mpeg",
            ["mpkg"] = "application/vnd.apple.installer+xml", ["odp"] = "application/vnd.oasis.opendocument.presentation",
            ["ods"] = "application/vnd.oasis.opendocument.spreadsheet", ["odt"] = "application/vnd.oasis.opendocument.text",
            ["oga"] = "audio/ogg", ["ogv"] = "video/ogg", ["ogx"] = "application/ogg", ["opus"] = "audio/ogg",
            ["otf"] = "font/otf", ["png"] = "image/png", ["pdf"] = "application/pdf",
            ["php"] = "application/x-httpd-php", ["ppt"] = "application/vnd.ms-powerpoint",
            ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ["rar"] = "application/vnd.rar", ["rtf"] = "application/rtf", ["sh"] = "application/x-sh",
            ["svg"] = "image/svg+xml", ["tar"] = "application/x-tar", ["tif"] = "image/tiff",
            ["tiff"] = "image/tiff", ["ts"] = "video/mp2t", ["ttf"] = "font/ttf", ["txt"] = "text/plain",
            ["vsd"] = "application/vnd.visio", ["wav"] = "audio/wav", ["wasm"] = "application/wasm",
            ["weba"] = "audio/webm", ["webm"] = "video/webm", ["webp"] = "image/webp",
            ["woff"] = "font/woff", ["woff2"] = "font/woff2", ["xhtml"] = "application/xhtml+xml",
            ["xls"] = "application/vnd.ms-excel",
            ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ["xml"] = "application/xml", ["xul"] = "application/vnd.mozilla.xul+xml",
            ["zip"] = "application/zip", ["3gp"] = "video/3gpp", ["3g2"] = "video/3gpp2",
            ["7z"] = "application/x-7z-compressed",
        };
    }
}