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

		public static WebServer ServeStatic(this WebServer server, string filepath, string path) =>
			server.RegisterHandler(Serve(filepath), path);

		static readonly Dictionary<string, string> MimeTypes = new Dictionary<string, string> {
			[".html"] = "text/html",
			[".txt"] = "text/plain"
		};
	}
}