using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Serac {
	// XXX: Make this ignore case for keys
	public class HeaderDictionary : IEnumerable<(string, string)> {
		readonly Dictionary<string, List<string>> Backing = new Dictionary<string, List<string>>();

		public string this[string key] {
			get {
				if(!Backing.ContainsKey(key)) throw new KeyNotFoundException();
				return Backing[key][0];
			}
			set {
				if(!Backing.ContainsKey(key)) Backing[key] = new List<string>();
				Backing[key].Add(value);
			}
		}

		public Dictionary<string, List<string>>.KeyCollection Keys => Backing.Keys;
		public Dictionary<string, List<string>>.ValueCollection Values => Backing.Values;

		public bool ContainsKey(string key) => Backing.ContainsKey(key);

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
		public IEnumerator<(string, string)> GetEnumerator() => Backing.SelectMany(kl => kl.Value.Select(v => (kl.Key, v))).GetEnumerator();
		public List<string> GetList(string key) => Backing.ContainsKey(key) ? Backing[key] : null;
	}
}