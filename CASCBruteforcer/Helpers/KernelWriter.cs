using CASCBruteforcer.Bruteforcers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CASCBruteforcer.Helpers
{
	public class KernelWriter
	{
		private string kernel;

		public KernelWriter(string Kernel)
		{
			kernel = Kernel;
		}

		public KernelWriter(byte[] data)
		{
			kernel = Encoding.ASCII.GetString(data);
		}


		public void Replace(string token, object value)
		{
			kernel = kernel.Replace("{" + token + "}", value.ToString());
		}

		public void ReplaceArray<T>(string token, IEnumerable<T> array)
		{
			kernel = kernel.Replace("{" + token + "}", string.Join(",", array));
			kernel = kernel.Replace("{" + token + "_SIZE}", array.Count().ToString());
		}

		public void ReplaceOffsetArray(IEnumerable<ulong> array)
		{
			var buckets = array.GroupBy(Jenkins96.HashSort).OrderBy(x => x.Key).ToDictionary(x => x.Key, x => (ushort)x.Count());
			ushort[] offsets = new ushort[256]; // offset of each first byte
			int maxbucket = buckets.Max(x => x.Value);

			ushort count = 0;
			foreach(var bucket in buckets)
			{
				offsets[bucket.Key] = count;
				count += bucket.Value;
			}

			Replace("BUCKET_SIZE", maxbucket); // biggest bucket size
			Replace("HASH_OFFSETS", string.Join(",", offsets));

			// apply hashes + pad to bucket size, prefix UL to remove the warnings..
			ReplaceArray("HASHES", array.Select(x => x + "UL")); 
		}


		public override string ToString() => kernel;
	}
}
