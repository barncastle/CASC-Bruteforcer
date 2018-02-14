using CASCBruteforcer.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CASCBruteforcer.Bruteforcers
{
	class VarientGen : IHash
	{
		const string CHECKFILES_URL = "https://bnet.marlam.in/checkFiles.php";

		private ListfileHandler ListfileHandler;
		private int ParallelFactor = 0;
		private bool DoLongRunning = false;

		private ConcurrentBag<string> FileNames;
		private ulong[] TargetHashes;
		private ushort[] HashesLookup;
		private ushort BucketSize;

		private ConcurrentQueue<string> ResultStrings;

		public void LoadParameters(params string[] args)
		{
			// parallel factor
			if (args.Length > 1)
				if (!int.TryParse(args[2].Trim(), out ParallelFactor))
					ParallelFactor = -1;

			DoLongRunning = (args.Length > 2 && args[2].Trim() == "1");

			// grab the known listfile
			ListfileHandler = new ListfileHandler();
			ListfileHandler.GetKnownListfile();

			if (!File.Exists("listfile.txt"))
				throw new Exception("No known listfile found.");

			FileNames = new ConcurrentBag<string>(File.ReadAllLines("listfile.txt").Select(x => Normalise(x)));

			// init variables
			ResultStrings = new ConcurrentQueue<string>();
			ParseHashes();
		}

		public void Start()
		{
			Console.WriteLine($"Starting Variation Generator ");

			GenerateWMOGroups();
			GenerateMaps();
			GenerateMapTextures();
			GenerateLodTextures();
			GenerateBakedTextures();
			GenerateModelVariants();
			GenerateSkins();

			if (DoLongRunning)
			{
				Console.WriteLine("Starting long running generators...");
				GenerateBones();
				GenerateWMOMinimaps();
				GenerateMinimaps();
			}

			LogAndExport();
		}


		#region Generators
		private void GenerateMaps()
		{
			string[] extensions = new string[] { ".TEX", ".WDL", ".WDT" };
			string[] lineendings = new string[] { "_OCC.WDT", "_LGT.WDT", "_FOGS.WDT" };

			var basefiles = FileNames.Where(x => x.StartsWith("WORLD\\MAPS\\") && x.EndsWith(".WDT")).Distinct();

			IEnumerable<string> files = Enumerable.Empty<string>();
			Parallel.ForEach(extensions, ext => files = files.Concat(basefiles.Select(x => Path.ChangeExtension(x, ext))));
			Parallel.ForEach(lineendings, ext => files = files.Concat(basefiles.Select(x => Path.ChangeExtension(x, ext))));
			files = files.Except(FileNames).Distinct();

			Console.WriteLine("  Generating Maps");
			Validate(files);
		}

		private void GenerateMapTextures()
		{
			string[] lineendings = new string[] { ".BLP", "_N.BLP" };

			var basefiles = FileNames.Where(x => x.StartsWith("WORLD\\MAPTEXTURES\\") && x.EndsWith(".BLP")).Select(x => x.Replace("_N.BLP", ".BLP").Replace(".BLP", "")).Distinct();

			IEnumerable<string> files = Enumerable.Empty<string>();
			Parallel.ForEach(lineendings, ext => files = files.Concat(basefiles.Select(x => x + ext)));
			files = files.Except(FileNames).Distinct();

			Console.WriteLine("  Generating Map Textures");
			Validate(files);
		}

		private void GenerateLodTextures()
		{
			string[] extensions = new string[] { ".BLP", ".WMO" };
			string[] lineendings = new string[] { ".BLP", "_L.BLP", "_E.BLP" };

			var basefiles = FileNames.Where(x => x.StartsWith("WORLD\\WMO\\") && HasExtension(x, extensions))
									.Select(x => PathWithoutExtension(x)).Distinct();

			IEnumerable<string> files = Enumerable.Empty<string>();
			Parallel.ForEach(lineendings, ext => files = files.Concat(basefiles.Select(x => x + ext)));
			files = files.Except(FileNames).Distinct();

			Console.WriteLine("  Generating LOD Textures");
			Validate(files);
		}

		private void GenerateBakedTextures()
		{
			const int RangeStart = 100000;
			const int RangeEnd = 200000;

			string[] lineendings = new string[] { "_HD.BLP", "_E.BLP", "_S.BLP", ".BLP" };
			string basefile = "TEXTURES\\BAKEDNPCTEXTURES\\CREATUREDISPLAYEXTRA-";

			IEnumerable<string> files = Enumerable.Range(RangeStart, RangeEnd).AsParallel().Select(x => basefile + x.ToString("00000"));
			Parallel.ForEach(lineendings, ext => files = files.Concat(files.Select(x => PathWithoutExtension(x) + ext)));
			files = files.Except(FileNames).Distinct();

			Console.WriteLine("  Generating Baked Textures");
			Validate(files);
		}

		private void GenerateModelVariants()
		{
			string[] extensions = new string[] { ".BLP", ".M2", ".PHYS", ".SKEL" };
			string[] splitextensions = new string[] { ".ANIM", ".BONE", ".SKIN" };

			string[] lineendings = new string[] { "_LOD01.SKIN", "_LOD02.SKIN", ".SKEL", ".PHYS", ".BLP", ".M2" };

			var basefiles = FileNames.Where(x => HasExtension(x, extensions)).Select(x => PathWithoutExtension(x));
			basefiles = basefiles.Concat(FileNames.Where(x => HasExtension(x, splitextensions) && x.Contains("_")).Select(x => TakeBeforeChar(x, '_')));
			basefiles = basefiles.Distinct();

			IEnumerable<string> files = Enumerable.Empty<string>();
			Parallel.ForEach(lineendings, ext => files = files.Concat(basefiles.Select(x => x + ext)));
			files = files.Except(FileNames).Distinct();

			Console.WriteLine("  Generating M2 Variants");
			Validate(files);
		}

		private void GenerateWMOGroups()
		{
			const int RangeStart = 0;
			const int RangeEnd = 50;
			Regex regex = new Regex(@"(.*?\d{3}(_?lod\d{1,2})?.wmo)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

			string[] lineendings = new string[] { "_LOD1.WMO", "_LOD2.WMO", "_LOD1.BLP", "_LOD2.BLP", "_LOD1_L.BLP", "_LOD2_L.BLP", "_LOD1_E.BLP", "_LOD2_E.BLP" };

			var basefiles = FileNames.Where(x => x.EndsWith(".WMO") && !regex.IsMatch(x)).Select(x => PathWithoutExtension(x).TrimEnd('_')).Distinct();

			IEnumerable<string> files = Enumerable.Empty<string>();
			Parallel.For(RangeStart, RangeEnd, i => files = files.Concat(basefiles.Select(x => x + "_" + i.ToString("000") + ".wmo")));
			files = files.Except(FileNames).Distinct();
			Parallel.ForEach(lineendings, ext => files = files.Concat(basefiles.Select(x => PathWithoutExtension(x))));
			files = files.Except(FileNames).Distinct();

			Console.WriteLine("  Generating WMO Groups");
			Validate(files);
		}


		private void GenerateBones()
		{
			string[] extensions = new string[] { ".M2", ".PHYS", ".SKEL" };
			var basefiles = FileNames.Where(x => HasExtension(x, extensions)).Select(x => PathWithoutExtension(x)).Distinct();

			IEnumerable<string> lineendings = Enumerable.Range(0, 100).Select(x => $"_{x.ToString("00")}.BONE");

			Console.WriteLine("  Generating M2 Bones");
			foreach (var basefile in basefiles)
			{
				IEnumerable<string> files = lineendings.AsParallel().Select(ext => basefile + ext);
				files = files.Except(FileNames).Distinct();
				Validate(files);
			}
		}

		private void GenerateSkins()
		{
			const int MaxRange = 10;

			string[] extensions = new string[] { ".M2", ".PHYS", ".SKEL" };
			var basefiles = FileNames.Where(x => HasExtension(x, extensions)).Select(x => PathWithoutExtension(x)).Distinct();

			IEnumerable<string> lineendings = Enumerable.Range(0, MaxRange).Select(x => $"{x.ToString("00")}.SKIN");
			lineendings = lineendings.Concat(new[] { "_LOD01.SKIN", "_LOD02.SKIN" });

			Console.WriteLine("  Generating M2 Skins");
			foreach (var lineending in lineendings)
			{
				IEnumerable<string> files = basefiles.AsParallel().Select(x => x + lineending);
				files = files.Except(FileNames).Distinct();

				Validate(files);
			}
		}

		private void GenerateMinimaps()
		{
			var basefiles = FileNames.Where(x => x.StartsWith("WORLD\\MAPS\\")).Select(x => new DirectoryInfo(x).Parent.Name).Distinct();
			IEnumerable<string> lineendings = Enumerable.Range(0, 64 * 64).Select(x => $"\\MAP{(x / 64).ToString("00")}_{(x % 64).ToString("00")}.BLP");

			Console.WriteLine("  Generating Minimaps");
			foreach (var le in lineendings)
			{
				IEnumerable<string> files = basefiles.AsParallel().Select(x => "WORLD\\MAPS\\" + x + le);
				files = files.Except(FileNames).Distinct();
				Validate(files);
			}
		}

		private void GenerateWMOMinimaps()
		{
			const int RangeEnd = 25;
			Regex regex = new Regex(@"(.*?\d{3}.wmo)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

			IEnumerable<string> lineendings = Enumerable.Range(0, RangeEnd * RangeEnd).Select(x => $"_{(x / RangeEnd).ToString("00")}_{(x % RangeEnd).ToString("00")}.BLP");

			var basefiles = FileNames.Where(x => x.EndsWith(".WMO") && regex.IsMatch(x)).Select(x => PathWithoutExtension(x).Replace("WORLD\\WMO\\","WORLD\\MINIMAPS\\WMO\\")).Distinct();

			Console.WriteLine("  Generating WMO Minimaps");
			foreach (var le in lineendings)
			{
				IEnumerable<string> files = basefiles.AsParallel().Select(x => x + le);
				files = files.Except(FileNames).Distinct();
				Validate(files);
			}
		}

		#endregion


		#region Validation
		private void Validate(IEnumerable<string> files)
		{
			Parallel.ForEach(files, x =>
			{
				ulong hash = new JenkinsHash().ComputeHash(x);
				if (Array.IndexOf(TargetHashes, hash, HashesLookup[hash & 0xFF], BucketSize) > -1)
				{
					ResultStrings.Enqueue(x);
					FileNames.Add(x); // add new files as we go
				}
			});
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
