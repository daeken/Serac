using System;
using System.IO;
using System.Threading.Tasks;

namespace Serac {
	public static class Extensions {
		public static void Deconstruct<T>(this T[] self, out T _0, out T _1) {
			if(self == null) throw new ArgumentNullException();
			if(self.Length != 2) throw new ArgumentOutOfRangeException();
			_0 = self[0];
			_1 = self[1];
		}

		public static (T, T) ToTuple<T>(this T[] self) {
			if(self == null) throw new ArgumentNullException();
			if(self.Length != 2) throw new ArgumentOutOfRangeException();
			return (self[0], self[1]);
		}

		public static async Task<byte[]> ReadAsync(this Stream stream, int count) {
			var data = new byte[count];
			await stream.ReadAsync(data, 0, count);
			return data;
		}
		
		public static Task WriteAsync(this Stream stream, byte[] data) =>
			stream.WriteAsync(data, 0, data.Length);
	}
}