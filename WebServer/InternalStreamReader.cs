using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Serac {
	internal class InternalStreamReader : IDisposable {
		readonly Stream Stream;
		byte? Previous;
		
		internal InternalStreamReader(Stream stream) => Stream = stream;

		public async Task<string> ReadLineAsync() {
			var temp = new byte[128];
			string ret = "";
			var i = 0;
			if(Previous is byte val) {
				temp[i++] = val;
				Previous = null;
			}

			while(true) {
				if(i == 128) {
					var cs = Encoding.ASCII.GetString(temp);
					ret = ret.Length == 0 ? cs : ret + cs;
					i = 0;
				}

				await Stream.ReadAsync(temp, i, 1);
				if(temp[i] == '\r') {
					await Stream.ReadAsync(temp, i, 1);
					if(temp[i] != '\n')
						Previous = temp[i];
					break;
				}
				i++;
			}

			if(i == 0) return ret;
			var cts = Encoding.ASCII.GetString(temp.Take(i).ToArray());
			return ret.Length == 0 ? cts : ret + cts;
		}

		public async Task<int> ReadAsync(byte[] data, int offset, int count) {
			var coffset = 0;
			if(Previous is byte pval && count > 0 && offset < data.Length) {
				data[offset] = pval;
				Previous = null;
				coffset = 1;
			}
			
			if(count > coffset)
				return await Stream.ReadAsync(data, offset + coffset, count - coffset) + coffset;
			return count;
		}

		public void Dispose() => Stream?.Dispose();
	}
}