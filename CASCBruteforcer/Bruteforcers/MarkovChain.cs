using CASCBruteforcer.Helpers;
using Markov;
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
	class MarkovChain : IHash
	{
		const string CHECKFILES_URL = "https://bnet.marlam.in/checkFiles.php";

		private ListfileHandler ListfileHandler;
		private int Limit = 100000;
		private string Directory;

		private string[][] FileNames;
		private string Extension;

		private HashSet<ulong> TargetHashes;
		private ConcurrentQueue<string> ResultStrings;

		public void LoadParameters(params string[] args)
		{
			if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
				throw new ArgumentException("No filter provided.");

			Directory = Normalise(args[1].Trim());

			if (args.Length < 3 || string.IsNullOrWhiteSpace(args[2]))
				throw new ArgumentException("No extension provided.");

			Extension = Normalise(args[2].Trim());

			if (!(args.Length > 3 && int.TryParse(args[3], out Limit)))
				Limit = 100000;

			ListfileHandler = new ListfileHandler();
			if (!ListfileHandler.GetKnownListfile(out string listfile))
				throw new Exception("No known listfile found.");

			FileNames = File.ReadAllLines(listfile).Select(x => Normalise(Path.GetFileNameWithoutExtension(x)).Split('_')).Distinct().ToArray();

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
			Console.WriteLine($"Starting Markov Chain ");

			var chain = new MarkovChain<string>(2);
			foreach (var file in FileNames)
				chain.Add(file);

			var rand = new Random();
			ConcurrentBag<string> queue = new ConcurrentBag<string>();

			for(int i = 0; i < Limit; i++)
			{
				string sentence = Path.Combine(Directory, string.Join("_", chain.Chain(rand))) + Extension;
				queue.Add(sentence);
				if (queue.Count > 200000)
					Validate(ref queue);
			}
		}



		#region Validation
		private void Validate(ref ConcurrentBag<string> files)
		{
			Parallel.ForEach(files, x =>
			{
				var j = new JenkinsHash();
				ulong hash = j.ComputeHash(x);
				if (TargetHashes.Contains(hash))
					ResultStrings.Enqueue(x);
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

					HttpWebRequest req = (System.Net.HttpWebRequest)WebRequest.Create(CHECKFILES_URL);
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

				TargetHashes = hashes.ToHashSet();
			}

			if (TargetHashes == null || TargetHashes.Count < 1)
				throw new ArgumentException("Unknown listfile is missing or empty");
		}

		#endregion

		#region Helpers
		private string Normalise(string s) => s.Trim().Replace("/", "\\").ToUpperInvariant();

		#endregion
	}
}
