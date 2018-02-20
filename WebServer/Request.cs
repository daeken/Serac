using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using static System.Console;

namespace Serac {
	public class Request {
		public readonly Stream Stream;
		public readonly StreamWriter StreamWriter;
		public readonly string RealPath, Method;
		public readonly Dictionary<string, string> Query;
		public readonly HeaderDictionary Headers;
		public readonly byte[] Body;
		public bool UseGzip;

		public string Path { get; internal set; }

		Request(Stream stream, StreamWriter sw, string path, string method, HeaderDictionary headers, byte[] body) {
			Stream = stream;
			StreamWriter = sw;
			if(path.Contains("?")) {
				string query;
				(RealPath, query) = path.Split('?', 2);
				Query = query.Split('&').Select(x => x.Split('=', 2)).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
			} else {
				RealPath = path;
				Query = new Dictionary<string, string>();
			}

			Method = method;
			Headers = headers;
			Body = body;
		}
		
		public static async Task<Request> Parse(Stream stream, StreamReader sr, StreamWriter sw) {
			var first = (await sr.ReadLineAsync())?.Split(' ');
			if(first == null || first.Length != 3)
				return null;
			var headers = new HeaderDictionary();
			while(true) {
				var line = await sr.ReadLineAsync();
				if(line == null)
					return null;
				if(line.Length == 0)
					break;
				var (k, v) = line.Split(':', 2);
				headers[k] = v.Trim();
			}

			var body = headers.ContainsKey("Content-Length") ? await stream.ReadAsync(int.Parse(headers["Content-Length"])) : null;
				
			return new Request(stream, sw, first[1], first[0], headers, body);
		}
	}
}