using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Serac.Static {
	public static class Static {
		public static Func<Request, Task<Response>> Serve(string filepath) {
			filepath += "/";
			return async request => {
				var sp = filepath + request.Path;
				if(request.Path.Contains("/../") || !File.Exists(sp))
					return null;
				if(!MimeTypes.TryGetValue(Path.GetExtension(sp), out var mime))
					mime = "text/plain";
				return new Response {
					StatusCode = 200, 
					ContentType = mime, 
					Data = await File.ReadAllBytesAsync(sp)
				};
			};
		}

		public static Func<Request, Task<Response>> ServeFile(string file) =>
			async request => {
				if(request.Path != "/" || !File.Exists(file)) return null;
				if(!MimeTypes.TryGetValue(Path.GetExtension(file), out var mime))
					mime = "text/plain";
				return new Response {
					StatusCode = 200, 
					ContentType = mime, 
					Data = await File.ReadAllBytesAsync(file)
				};
			};

		public static WebServer ServeStatic(this WebServer server, string filepath, string path) =>
			server.RegisterHandler(Serve(filepath), path);

		public static WebServer ServeStaticFile(this WebServer server, string file, string path) =>
			server.RegisterHandler(ServeFile(file), path);

		static readonly Dictionary<string, string> MimeTypes = new Dictionary<string, string> {
			[".html"] = "text/html",
			[".txt"] = "text/plain", 
			[".ico"] = "image/x-icon"
		};
	}
}