using CASCBruteforcer.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CASCBruteforcer.Bruteforcers
{
	class MixMatch : IHash
	{
		const string CHECKFILES_URL = "https://bnet.marlam.in/checkFiles.php";

		private ListfileHandler ListfileHandler;
		private int MaxDepth;
		private string FileFilter;
		private string[] FileNames;

		readonly private string[] Unwanted = new[]
		{
			"\\EXPANSION00\\", "\\EXPANSION01\\", "\\EXPANSION02\\", "\\EXPANSION03\\", "\\EXPANSION04\\", "\\EXPANSION05\\",
			"\\NORTHREND\\", "\\CATACLYSM\\", "\\PANDARIA\\", "\\OUTLAND\\","\\PANDAREN\\",
			"WORLD\\MAPTEXTURES\\", "WORLD\\MINIMAPS\\", "CHARACTER\\", "\\BAKEDNPCTEXTURES\\", "COMPONENTS\\"
		};
		readonly private string[] Extensions = new[] { ".OGG", ".BLP", ".M2", ".WMO", ".MP3", ".BLS" };

		private ulong[] TargetHashes;
		private ushort[] HashesLookup;
		private ushort BucketSize;

		private ConcurrentQueue<string> ResultStrings;

		public void LoadParameters(params string[] args)
		{
			FileFilter = args.Length > 1 ? args[0] : "";

			if (args.Length < 2 || !int.TryParse(args[1], out MaxDepth))
				MaxDepth = 5;
			if (MaxDepth < 1)
				MaxDepth = 1;

			ListfileHandler = new ListfileHandler();
			if (!ListfileHandler.GetKnownListfile(out string listfile))
				throw new Exception("No known listfile found.");

			FileNames = File.ReadAllLines(listfile).Select(x => Normalise(x)).Distinct().ToArray();

			ResultStrings = new ConcurrentQueue<string>();
			ParseHashes();
		}

		public void Start()
		{
			Run();
			LogAndExport();
		}

		private void Run()
		{
			Console.WriteLine($"Starting MixMatch ");
			Console.WriteLine("Loading Dictionary...");

			// store all line endings
			HashSet<string> endings = new HashSet<string>();
			foreach (var o in FileNames)
			{
				int _s = o.Length - o.Replace("_", "").Length; // underscore count
				var parts = Path.GetFileNameWithoutExtension(o).Split('_').Reverse();

				for (int i = 1; i <= _s && i <= MaxDepth; i++) // split by _s up to depth
				{
					endings.Add(string.Join("_", parts.Take(i).Reverse())); // exclude prefixed underscore
					endings.Add("_" + string.Join("_", parts.Take(i).Reverse())); // prefix underscore
				}
			}

			Console.WriteLine("Loading Filenames...");

			// load files we want to permute
			Queue<string> formattednames = new Queue<string>(FileNames.Where(x => !Unwanted.Any(y => x.Contains(y)) && x.ToLower().EndsWith(FileFilter)).Distinct());

			if (formattednames.Count == 0)
				throw new Exception($"No filenames match the provided filter `{FileFilter}`.");
			
			while (formattednames.Count > 0)
			{
				ConcurrentBag<string> queue = new ConcurrentBag<string>();

				string o = formattednames.Dequeue();
				int _s = o.Length - o.Replace("_", "").Length; // underscore count

				var parts = Path.GetFileNameWithoutExtension(o).Split('_').Reverse();
				string path = Path.GetDirectoryName(o);

				// suffix known endings at each underscore
				for (int i = 0; i <= _s && i <= MaxDepth; i++)
				{
					string temp = string.Join("_", parts.Skip(i).Reverse());
					Parallel.ForEach(endings, e =>
					{
						queue.Add(path + temp + e);
						queue.Add(path + "_" + temp + e);
					});

					Validate(ref queue);
				}
			}
		}


		#region Validation
		private void Validate(ref ConcurrentBag<string> files)
		{
			Parallel.ForEach(files, x =>
			{
				var j = new JenkinsHash();
				foreach (var e in Extensions)
				{
					ulong hash = j.ComputeHash(x + e); // try each extension
					if (Array.IndexOf(TargetHashes, hash, HashesLookup[hash & 0xFF], BucketSize) > -1)
						ResultStrings.Enqueue(x);
				}
			});

			files = new ConcurrentBag<string>();
		}

		private void PostResults()
		{
			const int TAKE = 20000;

			int count = (int)Math.Ceiling(ResultStrings.Count / (float)TAKE);
			for (int i = 0; i < count; i++)
			{
				try
				{
					byte[] data = Encoding.ASCII.GetBytes("files=" + string.Join("\r\n", ResultStrings.Skip(i * TAKE).Take(TAKE)));

					HttpWebRequest req = (HttpWebRequest)WebRequest.Create(CHECKFILES_URL);
					req.Method = "POST";
					req.ContentType = "application/x-www-form-urlencoded";
					req.ContentLength = data.Length;
					req.UserAgent = "CASCBruteforcer/1.0 (+https://github.com/barncastle/CASC-Bruteforcer)"; // for tracking purposes
					using (var stream = req.GetRequestStream())
					{
						stream.Write(data, 0, data.Length);
						req.GetResponse(); // send the post
					}

					req.Abort();
				}
				catch { }
			}
		}

		private void LogAndExport()
		{
			ResultStrings = new ConcurrentQueue<string>(ResultStrings.Distinct());

			// log completion
			Console.WriteLine($"Found {ResultStrings.Count}:");

			if (ResultStrings.Count > 0)
			{
				// print to the screen
				foreach (var r in ResultStrings)
					Console.WriteLine($"  {r.Replace("\\", "/").ToLower()}");

				// write to Output.txt
				using (var sw = new StreamWriter(File.OpenWrite("Output.txt")))
				{
					sw.BaseStream.Position = sw.BaseStream.Length;
					foreach (var r in ResultStrings)
						sw.WriteLine(r.Replace("\\", "/").ToLower());
				}

				// post to Marlamin's site
				PostResults();
			}

			Console.WriteLine("");
		}

		#endregion


		#region Unknown Hash Functions
		private void ParseHashes()
		{
			bool parseListfile = ListfileHandler.GetUnknownListfile("unk_listfile.txt", "");
			if (parseListfile)
			{
				string[] lines = new string[0];

				// sanity check it actually exists
				if (File.Exists("unk_listfile.txt"))
					lines = File.ReadAllLines("unk_listfile.txt");

				// parse items - hex and standard because why not
				ulong dump = 0;
				IEnumerable<ulong> hashes = new ulong[0]; // 0 hash is used as a dump
#if DEBUG
				hashes = hashes.Concat(new ulong[] { 4097458660625243137, 13345699920692943597 }); // test hashes for the README examples
#endif
				hashes = hashes.Concat(lines.Where(x => ulong.TryParse(x.Trim(), NumberStyles.HexNumber, null, out dump)).Select(x => dump)); // hex
				hashes = hashes.Concat(lines.Where(x => ulong.TryParse(x.Trim(), out dump)).Select(x => dump)); // standard
				hashes = hashes.Distinct().OrderBy(Jenkins96.HashSort).ThenBy(x => x);

				TargetHashes = hashes.ToArray();

				BuildLookup();
			}

			if (TargetHashes == null || TargetHashes.Length < 1)
				throw new ArgumentException("Unknown listfile is missing or empty");
		}

		private void BuildLookup()
		{
			var buckets = TargetHashes.GroupBy(Jenkins96.HashSort).OrderBy(x => x.Key).ToDictionary(x => x.Key, x => (ushort)x.Count());
			HashesLookup = new ushort[256]; // offset of each first byte
			BucketSize = buckets.Max(x => x.Value);

			ushort count = 0;
			foreach (var bucket in buckets)
			{
				HashesLookup[bucket.Key] = count;
				count += bucket.Value;
			}

			Array.Resize(ref TargetHashes, TargetHashes.Length + BucketSize);
		}

		#endregion

		#region Helpers
		private string Normalise(string s) => s.Trim().Replace("/", "\\").ToUpperInvariant();

		private string PathWithoutExtension(string s) => Path.Combine(Path.GetDirectoryName(s), Path.GetFileNameWithoutExtension(s));

		private bool HasExtension(string file, string[] extensions) => Array.BinarySearch(extensions, Path.GetExtension(file)) > -1;

		private string TakeBeforeChar(string file, char c) => file.Contains(c) ? file.Substring(0, file.LastIndexOf(c)) : file;
		#endregion
	}
}
