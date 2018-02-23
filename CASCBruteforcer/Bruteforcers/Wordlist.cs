using System;
using CASCBruteforcer.Algorithms;
using CASCBruteforcer.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Globalization;
using System.Net;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace CASCBruteforcer.Bruteforcers
{
	class Wordlist : IHash
	{
		const string CHECKFILES_URL = "https://bnet.marlam.in/checkFiles.php";

		private ListfileHandler ListfileHandler;
		private string[] Masks;
		private string[] Words;
		private uint ParallelFactor = 0;

		private ulong[] TargetHashes;
		private ushort[] HashesLookup;
		private ushort BucketSize;

		private ConcurrentQueue<string> ResultStrings;

		public void LoadParameters(params string[] args)
		{
			if (args.Length < 2)
				throw new ArgumentException("Incorrect number of arguments");

			// format + validate template masks
			if (File.Exists(args[1]))
			{
				Masks = File.ReadAllLines(args[1]).Select(x => Normalise(x)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
			}
			else if (!string.IsNullOrWhiteSpace(args[1]))
			{
				Masks = new string[] { Normalise(args[1]) };
			}

			if (Masks == null || Masks.Length == 0)
				throw new ArgumentException("No valid masks.");

			// parallel factor
			if (args.Length > 2)
				uint.TryParse(args[2].Trim(), out ParallelFactor);

			// grab the listfile, either the one from Marla or use the supplied argument
			Console.WriteLine($"Starting Wordlist ");
			Console.WriteLine("Loading Dictionary...");

			ListfileHandler = new ListfileHandler();
			if (ListfileHandler.GetKnownListfile(out string listfile, args.Length > 3 ? args[3] : ""))
			{
				Regex capitalisedsplit = new Regex(@"(?<!^)(?=[A-Z.-\/_\s])", RegexOptions.Compiled); // splits by capitals and the standard stuff
				Regex isCapitalised = new Regex(@"([A-Z][a-z])", RegexOptions.Compiled); // find upper next to lower

				var lines = File.ReadAllLines(listfile);

				var words = lines.SelectMany(x => x.Split(new char[] { '_', '/', ' ', '-', '.' })).Concat(new[] { "_", "/", " ", "-", "." });
				if (lines.Any(x => isCapitalised.IsMatch(x))) // check we need to run the capitalised split
					words = words.Concat(lines.SelectMany(x => capitalisedsplit.Split(x)).Except(words));

				Words = words.Distinct().ToArray();
				Array.Resize(ref lines, 0); // delete the old strings
			}
			else
			{
				throw new Exception("Unable to generate a wordlist.");
			}

			// init variables
			ResultStrings = new ConcurrentQueue<string>();
			ParseHashes();
		}

		public void Start()
		{
			if (ParallelFactor > 0)
			{
				Parallel.For(0, Masks.Length, new ParallelOptions() { MaxDegreeOfParallelism = (int)ParallelFactor }, i => Run(i));
			}
			else
			{
				for (int i = 0; i < Masks.Length; i++)
					Run(i);
			}

			LogAndExport();
		}

		private void Run(int m)
		{
			string mask = Normalise(Masks[m]);

			if (!mask.Contains("%"))
			{
				JenkinsHash j = new JenkinsHash();
				if (TargetHashes.Contains(j.ComputeHash(mask)))
					ResultStrings.Enqueue(mask);
			}

			// Start the work
			if (ParallelFactor > 0)
			{
				Parallel.ForEach(Words, x =>
				{
					string temp = Normalise(mask.Replace("%", x));
					ulong hash = new JenkinsHash().ComputeHash(temp);
					if (Array.IndexOf(TargetHashes, hash, HashesLookup[hash & 0xFF], BucketSize) > -1)
						ResultStrings.Enqueue(temp);
				});
			}
			else
			{
				JenkinsHash j = new JenkinsHash();

				var found = from word in Words.Select(x => Normalise(mask.Replace("%", x)))
							let h = j.ComputeHash(word)
							where Array.IndexOf(TargetHashes, h, HashesLookup[h & 0xFF], BucketSize) > -1
							select word;

				foreach (var f in found)
					ResultStrings.Enqueue(f);
			}
		}


		#region Validation
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
		#endregion
	}
}