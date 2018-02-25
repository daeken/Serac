using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Serac.Katatonic {
	public class Session {
		static readonly byte[] MacKey, AesKey;
		static RNGCryptoServiceProvider Rng;

		static byte[] GetOrGenerateKey(string fn, int size) {
			if(File.Exists(fn)) return File.ReadAllBytes(fn);
			using(var fp = File.OpenWrite(fn)) {
				var data = new byte[size];
				Rng.GetBytes(data);
				fp.Write(data, 0, size);
				return data;
			}
		}

		[MethodImpl(MethodImplOptions.NoOptimization)]
		static bool SecureCompare(byte[] a, byte[] b) {
			if(a.Length != b.Length) return false;
			var len = a.Length;
			var cur = true;
			for(var i = 0; i < len; ++i)
				cur = cur && a[i] == b[i];
			return cur;
		}

		static byte[] AddPadding(byte[] data) {
			var off = data.Length;
			var plen = 16 - off % 16;
			if(plen == 0) plen = 16;
			var ret = new byte[off + plen];
			Array.Copy(data, ret, off);
			for(var i = 0; i < plen; ++i)
				ret[off + i] = (byte) plen;
			return ret;
		}

		static int CheckPadding(byte[] data) {
			var end = data.Length - 1;
			var plen = data[end];
			for(var i = 1; i < plen; ++i)
				if(data[end - i] != plen) return -1;
			return data.Length - plen;
		}

		static Session() {
			Rng = new RNGCryptoServiceProvider();
			MacKey = GetOrGenerateKey("mac.secret", 32);
			AesKey = GetOrGenerateKey("aes.secret", 32);
		}
		
		string CurCookie;

		Dictionary<string, string> IValues;
		public Dictionary<string, string> Values {
			get {
				if(IValues != null) return IValues;
				return IValues = CurCookie == null ? new Dictionary<string, string>() : DecryptCookie(CurCookie);
			}
		}

		Dictionary<string, string> DecryptCookie(string cookie) {
			try {
				var data = Convert.FromBase64String(cookie);
				var hmac = new HMACSHA256(MacKey).ComputeHash(data, 0, data.Length - 32);
				if(!SecureCompare(hmac, data.Skip(data.Length - 32).ToArray()))
					return new Dictionary<string, string>();
				var kiv = new byte[48];
				var tiv = data.Take(16).ToArray();
				using(var rijn = new RijndaelManaged()) {
					rijn.Padding = PaddingMode.None;
					var dec = rijn.CreateDecryptor(AesKey, tiv);
					using(var ms = new MemoryStream(data.Skip(16).Take(48).ToArray()))
						using(var cs = new CryptoStream(ms, dec, CryptoStreamMode.Read))
							cs.Read(kiv, 0, 48);
				}

				var pkey = kiv.Take(32).ToArray();
				var piv = kiv.Skip(32).Take(16).ToArray();

				var pdata = new byte[data.Length - 64 - 32];
				using(var rijn = new RijndaelManaged()) {
					rijn.Padding = PaddingMode.None;
					var dec = rijn.CreateDecryptor(pkey, piv);
					var dlen = data.Length - 64 - 32;
					using(var ms = new MemoryStream(data.Skip(64).Take(pdata.Length).ToArray()))
						using(var cs = new CryptoStream(ms, dec, CryptoStreamMode.Read))
							cs.Read(pdata, 0, pdata.Length);
				}

				var rlen = CheckPadding(pdata);
				if(rlen == -1) return new Dictionary<string, string>();
				var pstr = Encoding.UTF8.GetString(pdata, 0, rlen);
				return pstr.Split('&').Select(x => x.Split('=', 2)).Where(x => x.Length == 2)
					.ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
			} catch {
				return new Dictionary<string, string>();
			}
		}

		string EncryptCookie(Dictionary<string, string> values) {
			if(values == null || values.Count == 0) return null;
			var pstr = values.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}").Join("&");
			var pdata = Encoding.UTF8.GetBytes(pstr);
			
			var pkey = new byte[32];
			var piv = new byte[16];
			lock(Rng) {
				Rng.GetBytes(pkey);
				Rng.GetBytes(piv);
			}

			pdata = AddPadding(pdata);

			byte[] cdata;
			using(var rijn = new RijndaelManaged()) {
				rijn.Padding = PaddingMode.None;
				var enc = rijn.CreateEncryptor(pkey, piv);
				using(var ms = new MemoryStream()) {
					using(var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
						cs.Write(pdata, 0, pdata.Length);
					cdata = ms.ToArray();
				}
			}

			byte[] kiv;
			var tiv = new byte[16];
			using(var rijn = new RijndaelManaged()) {
				rijn.Padding = PaddingMode.None;
				lock(Rng)
					Rng.GetBytes(tiv);
				var enc = rijn.CreateEncryptor(AesKey, tiv);
				using(var ms = new MemoryStream()) {
					using(var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write)) {
						cs.Write(pkey, 0, 32);
						cs.Write(piv, 0, 16);
					}
					kiv = ms.ToArray();
				}
			}
			
			var premac = new[] { tiv, kiv, cdata }.SelectMany(x => x).ToArray();
			var hmac = new HMACSHA256(MacKey).ComputeHash(premac);
			
			return Convert.ToBase64String(new[] { premac, hmac }.SelectMany(x => x).ToArray());
		}
		
		public Session(Request request) {
			CurCookie = request.Cookies.ContainsKey("KSESS") ? request.Cookies["KSESS"] : null;
		}

		public void SetCookie(Response response) {
			if(IValues == null || IValues.Count == 0) return;
			response.Cookies["KSESS"] = EncryptCookie(IValues);
		}
	}
}