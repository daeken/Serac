using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static System.Console;

namespace Serac.WebSockets {
	public class WebSocket {
		internal class CloseException : Exception {}
		readonly Stream Stream;

		public event EventHandler<bool> Disconnect;
		bool Closed, ClientClose;
		readonly bool UsingCompression;
		
		internal WebSocket(Stream stream, bool usingCompression) {
			Stream = stream;
			UsingCompression = usingCompression;
		}

		public async Task<string> ReadText() => Encoding.UTF8.GetString(await ReadBinary());

		public async Task<byte[]> ReadBinary() {
			var ret = new List<byte[]>();
			var compressed = false;
			while(true) {
				var (final, fcompressed, opcode, data) = await ReadFrame();
				compressed = compressed || fcompressed;
				ret.Add(data);
				if(final)
					break;
			}
			if(compressed && UsingCompression)
				ret.Add(new byte[] { 0x00, 0x00, 0xff, 0xff });
			var td = ret.SelectMany(x => x).ToArray();
			if(compressed && UsingCompression) {
				var oret = new List<byte[]>();
				using(var ds = new DeflateStream(new MemoryStream(td), CompressionMode.Decompress)) {
					while(true) {
						var chunk = new byte[65536];
						var rlen = await ds.ReadAsync(chunk, 0, chunk.Length);
						if(rlen == 0)
							break;
						oret.Add(rlen == 65536 ? chunk : chunk.Take(rlen).ToArray());
					}
					return oret.SelectMany(x => x).ToArray();
				}
			}
			return td;
		}

		public async Task Write(string message) {
			var data = Encoding.UTF8.GetBytes(message);
			await WriteSplit(data, 1);
		}

		public async Task Write(byte[] data) {
			await WriteSplit(data, 2);
		}

		async Task WriteSplit(byte[] data, int opcode) {
			// XXX: Compressed packets need compression adjustment.
			/*if(UsingCompression) {
				using(var ms = new MemoryStream()) {
					using(var rms = new MemoryStream(data))
						using(var ds = new DeflateStream(ms, CompressionLevel.Fastest)) {
							await rms.CopyToAsync(ds);
							await ds.FlushAsync();
						}

					await ms.FlushAsync();
					data = ms.ToArray();
				}
			}*/
			for(var i = 0; i < data.Length; i += 32768)
				await WriteFrame(i + 32768 >= data.Length, /*UsingCompression*/ false, i == 0 ? opcode : 0, data.Skip(i).Take(32768).ToArray());
		}

		async Task<(bool, bool, int, byte[])> ReadFrame() {
			while(true) {
				var start = await Stream.ReadAsync(2);
				var final = start[0] >> 7 == 1;
				var compressed = (start[0] & 0x40) != 0;
				var opcode = start[0] & 0xF;
				if(opcode == 8) {
					ClientClose = true;
					throw new CloseException();
				}

				var plen = start[1] & 0x7F;
				if(plen == 126) {
					var slen = await Stream.ReadAsync(2);
					plen = (slen[0] << 8) | slen[1];
				} else if(plen == 127) {
					var slen = await Stream.ReadAsync(8);
					Array.Reverse(slen);
					var temp = BitConverter.ToUInt64(slen, 0);
					if(temp > int.MaxValue) throw new OverflowException();
					plen = (int) temp;
				}

				var mask = start[1] >> 7 == 1 ? await Stream.ReadAsync(4) : null;
				var data = await Stream.ReadAsync(plen);

				if(mask != null)
					for(var i = 0; i < plen; ++i)
						data[i] ^= mask[i & 3];

				if(opcode != 9) return (final, compressed, opcode, data);

				await WriteFrame(final, false, 10, data);
			}
		}

		async Task WriteFrame(bool final, bool compressed, int opcode, byte[] data) {
			await Stream.WriteAsync(new [] { (byte) (opcode | ((compressed ? 1 : 0) << 6) | ((final ? 1 : 0) << 7)), (byte) (data.Length < 126 ? data.Length : 126) });
			if(data.Length >= 126)
				await Stream.WriteAsync(new [] { (byte) (data.Length & 0xFF), (byte) (data.Length >> 8) });
			if(data.Length > 0)
				await Stream.WriteAsync(data);
			await Stream.FlushAsync();
		}

		public async Task Close() {
			if(!Closed) {
				Closed = true;
				await WriteFrame(true, false, 8, new byte[0]);
				Disconnect?.Invoke(this, ClientClose);
				Stream.Close();
				throw new CloseException();
			}
		}
	}
	
	public static class WebSockets {
		public static Func<Request, Task<Response>> Serve(Func<WebSocket, Request, Task> handler, bool enableCompression=true) {
			return async request => {
				if(request.Method != "GET" || !request.Headers.ContainsKey("Connection") ||
				   request.Headers["Connection"] != "Upgrade")
					return null;

				var pext = request.Headers.GetList("Sec-WebSocket-Extensions")?.Join(",")?.Split(",") ?? Enumerable.Empty<string>();
				pext = pext.Select(x => x.Trim()).Where(x => x.Length != 0);
				var extensions = pext.Select(x => x.Split(';').Select(y => y.Trim())).ToDictionary(x => x.First(), x => x.Skip(1).ToArray());
				var acceptedExtensions = new List<string>();
				var usingCompression = false;
				if(enableCompression && extensions.ContainsKey("permessage-deflate")) {
					acceptedExtensions.Add("permessage-deflate");
					usingCompression = true;
				}
				
				await new Response {
					StatusCode = 101,
					Headers = {
						["Upgrade"] = "websocket",
						["Connection"] = "Upgrade",
						["Sec-WebSocket-Accept"] = Convert.ToBase64String(SHA1.Create().ComputeHash(
							Encoding.ASCII.GetBytes(request.Headers["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))), 
						["Sec-WebSocket-Protocol"] = request.Headers.TryGetValue("Sec-WebSocket-Protocol"), 
						["Sec-WebSocket-Extensions"] = acceptedExtensions.Count == 0 ? null : acceptedExtensions.Join(", ")
					}
				}.Send(request.Stream, request.StreamWriter);
				
				var ws = new WebSocket(request.Stream, usingCompression);
				try {
					await handler(ws, request);
				} catch(WebSocket.CloseException) {
					try {
						await ws.Close();
					} catch(WebSocket.CloseException) {
						// Expected
					}
				}

				return null;
			};
		}

		public static Func<Request, Task<Response>> Serve(Func<WebSocket, Task> handler, bool enableCompression=true) =>
			Serve((ws, request) => handler(ws), enableCompression: enableCompression);

		public static WebServer WebSocket(this WebServer server, string path, Func<WebSocket, Request, Task> handler, bool enableCompression=true) =>
			server.RegisterHandler(path, Serve(handler, enableCompression: enableCompression));

		public static WebServer WebSocket(this WebServer server, string path, Func<WebSocket, Task> handler, bool enableCompression=true) =>
			server.RegisterHandler(path, Serve(handler, enableCompression: enableCompression));
	}
}