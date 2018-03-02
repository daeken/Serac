using System;
using System.Collections.Generic;
using System.Web;

namespace Serac.Template {
	public static class PublicExtensions {
		public static string SanitizeQuote(this string data) =>
			data.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
		public static string SanitizeInterp(this string data) =>
			data.Replace("{", "{{").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
		public static string SanitizeVerbatim(this string data) =>
			data.Replace("\"", "\"\"");
		public static string HtmlEncode(this string data) =>
			HttpUtility.HtmlEncode(data);
	}
	
	internal static class Extensions {
		public static IEnumerable<(int, T)> Enumerate<T>(this IEnumerable<T> seq) {
			var i = 0;
			foreach(var elem in seq)
				yield return (i++, elem);
		}
		
		internal static (string, string) GetUntil(this string str, string ends) {
			var pos = str.CSIndexOf(ends);
			return pos == -1 ? (str, null) : (str.Substring(0, pos), str.Substring(pos));
		}

		internal static int CSIndexOf(this string str, string any, int start=0) {
			for(var i = start; i < str.Length; ++i) {
				switch(str[i]) {
					case '(':
						i = str.CSIndexOf(")", i + 1);
						break;
					case '{':
						i = str.CSIndexOf("}", i + 1);
						break;
					case '[':
						i = str.CSIndexOf("]", i + 1);
						break;
					case '$':
						i = str.EndOfInterpolatedString(i + 2);
						break;
					case '@':
						i = str.EndOfVerbatimString(i + 2);
						break;
					case '"':
						i = str.EndOfString(i + 1);
						break;
					case '\'':
						break;
					default:
						if(any.Contains(str[i].ToString()))
							return i;
						break;
				}
				if(i == -1)
					throw new ArgumentException();
			}
			return -1;
		}

		static int EndOfString(this string str, int start) {
			for(var i = start; i < str.Length; ++i) {
				switch(str[i]) {
					case '"':
						return i;
					case '\\':
						i++;
						break;
				}
			}
			return -1;
		}

		static int EndOfVerbatimString(this string str, int start) {
			for(var i = start; i < str.Length; ++i) {
				if(str[i] == '"') {
					if(i + 1 >= str.Length || str[i + 1] != '"')
						return i;
					i++;
				}
			}
			return -1;
		}

		static int EndOfInterpolatedString(this string str, int start) {
			for(var i = start; i < str.Length; ++i) {
				switch(str[i]) {
					case '"':
						return i;
					case '\\':
						i++;
						break;
					case '{':
						if(str[i + 1] == '{') {
							i++;
							break;
						}

						i = str.CSIndexOf("}", i + 1);
						break;
				}
			}
			return -1;
		}
	}
}