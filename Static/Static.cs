using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Serac.Static {
	public static class StaticContent {
		static bool Cache, CacheRefresh = true;
		static float MinimumCompression = 0.9f;
		static readonly Dictionary<string, (DateTime FileTime, Response Response)> FileCache = new Dictionary<string, (DateTime, Response)>();

		public static WebServer EnableCacheRefresh(this WebServer server) =>
			server.InlineUpdate(() => CacheRefresh = true);
		public static WebServer DisableCacheRefresh(this WebServer server) =>
			server.InlineUpdate(() => CacheRefresh = false);

		public static WebServer EnableStaticCache(this WebServer server) =>
			server.InlineUpdate(() => Cache = true);
		public static WebServer DisableStaticCache(this WebServer server) =>
			server.InlineUpdate(() => Cache = false);

		public static WebServer SetStaticCompressionMinimum(this WebServer server, float ratio) =>
			server.InlineUpdate(() => MinimumCompression = ratio);

		static async Task<Response> SendFile(string file, bool gzip) {
			if(FileCache.ContainsKey(file) && (!CacheRefresh || FileCache[file].FileTime == File.GetLastWriteTime(file)))
				return FileCache[file].Response;

			if(!MimeTypes.TryGetValue(Path.GetExtension(file), out var mime))
				mime = "text/plain";

			var data = await File.ReadAllBytesAsync(file);
			var resp = new Response {
				StatusCode = 200, 
				ContentType = mime, 
				Data = data
			};
			if(gzip) {
				using(var wms = new MemoryStream()) {
					using(var rms = new MemoryStream(data))
					using(var gs = new GZipStream(wms, CompressionLevel.Fastest)) {
						await rms.CopyToAsync(gs);
						await gs.FlushAsync();
					}
					var cdata = wms.ToArray();
					if(cdata.Length < data.Length * MinimumCompression) {
						resp.Data = cdata;
						resp.Gzipped = true;
					} else
						resp.NoCompression = true;
				}
			}
			
			if(Cache) FileCache[file] = (File.GetLastWriteTime(file), resp);
			return resp;
		}
		
		public static Func<Request, Task<Response>> Serve(string filepath) {
			filepath += "/";
			return async request => {
				var sp = filepath + request.Path;
				if(request.Path.Contains("/../") || !File.Exists(sp))
					return null;
				return await SendFile(sp, request.UseGzip);
			};
		}

		public static Func<Request, Task<Response>> ServeFile(string file) =>
			async request => request.Path != "/" || !File.Exists(file) ? null : await SendFile(file, request.UseGzip);

		public static WebServer Static(this WebServer server, string path, string filepath) =>
			server.RegisterHandler(path, Serve(filepath));

		public static WebServer StaticFile(this WebServer server, string path, string file) =>
			server.RegisterHandler(path, ServeFile(file));

		static readonly Dictionary<string, string> MimeTypes = new Dictionary<string, string> {
			[".html"] = "text/html",
			[".txt"] = "text/plain", 
			[".ico"] = "image/x-icon"
		};
	}
}