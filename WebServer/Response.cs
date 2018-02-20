using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Serac {
	public class Response {
		static readonly Dictionary<int, string> StatusCodes = new Dictionary<int, string> {
			[100] = "Continue", 
			[101] = "Switching Protocols", 
			[200] = "OK", 
			[201] = "Created", 
			[202] = "Accepted", 
			[203] = "Non-Authoritative Information", 
			[204] = "No Content", 
			[205] = "Reset Content", 
			[206] = "Partial Content", 
			[300] = "Multiple Choices", 
			[301] = "Moved Permanently", 
			[302] = "Found", 
			[303] = "See Other", 
			[304] = "Not Modified", 
			[305] = "Use Proxy", 
			[307] = "Temporary Redirect", 
			[400] = "Bad Request", 
			[401] = "Unauthorized", 
			[402] = "Payment Required", 
			[403] = "Forbidden", 
			[404] = "Not Found", 
			[405] = "Method Not Allowed", 
			[406] = "Not Acceptable", 
			[407] = "Proxy Authentication Required", 
			[408] = "Request Timeout", 
			[409] = "Conflict", 
			[410] = "Gone", 
			[411] = "Length Required", 
			[412] = "Precondition Failed", 
			[413] = "Payload Too Large", 
			[414] = "URI Too Long", 
			[415] = "Unsupported Media Type", 
			[416] = "Range Not Satisfiable", 
			[417] = "Expectation Failed", 
			[418] = "I'm a teapot", 
			[426] = "Upgrade Required", 
			[500] = "Internal Server Error", 
			[501] = "Not Implemented", 
			[502] = "Bad Gateway", 
			[503] = "Service Unavailable", 
			[504] = "Gateway Time-out", 
			[505] = "HTTP Version Not Supported", 
			[102] = "Processing", 
			[207] = "Multi-Status", 
			[226] = "IM Used", 
			[308] = "Permanent Redirect", 
			[422] = "Unprocessable Entity", 
			[423] = "Locked", 
			[424] = "Failed Dependency", 
			[428] = "Precondition Required", 
			[429] = "Too Many Requests", 
			[431] = "Request Header Fields Too Large", 
			[451] = "Unavailable For Legal Reasons", 
			[506] = "Variant Also Negotiates", 
			[507] = "Insufficient Storage", 
			[511] = "Network Authentication Required"
		};

		public int StatusCode = 200;
		public readonly HeaderDictionary Headers = new HeaderDictionary();
		public byte[] Data;
		public bool Gzipped, NoCompression;

		public string Body {
			set => Data = Encoding.UTF8.GetBytes(value);
		}

		public string ContentType {
			get => Headers.ContainsKey("Content-Type") ? Headers["Content-Type"] : null;
			set => Headers["Content-Type"] = value;
		}

		public async Task Send(Stream stream, StreamWriter sw) {
			await sw.WriteAsync($"HTTP/1.1 {StatusCode} {(StatusCodes.ContainsKey(StatusCode) ? StatusCodes[StatusCode] : "Unknown")}\r\n");
			if(Data != null && !Headers.ContainsKey("Content-Length"))
				Headers["Content-Length"] = Data.Length.ToString();
			foreach(var (k, v) in Headers)
				if(v != null)
					await sw.WriteAsync($"{k}: {v}\r\n");
			await sw.WriteAsync("\r\n");
			await sw.FlushAsync();

			if(Data != null) {
				await stream.WriteAsync(Data, 0, Data.Length);
				await stream.FlushAsync();
			}
		}
	}
}