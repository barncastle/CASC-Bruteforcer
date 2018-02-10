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

namespace CASCBruteforcer.Bruteforcers
{
	class Wordlist : IHash
	{
		const string CHECKFILES_URL = "https://bnet.marlam.in/checkFiles.php";

		private ListfileHandler ListfileHandler;
		private string[] Masks;
		private ulong[] TargetHashes;
		private string[] Words;
		private uint ParallelFactor = 0;

		private ConcurrentQueue<string> ResultStrings;

		public void LoadParameters(params string[] args)
		{
			if (args.Length < 2)
				throw new ArgumentException("Incorrect number of arguments");

			// format + validate template masks
			if (File.Exists(args[1]))
			{
				Masks = File.ReadAllLines(args[1]).Select(x => Normalise(x)).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
			}
			else if (!string.IsNullOrWhiteSpace(args[1]))
			{
				Masks = new string[] { Normalise(args[1]) };
			}

			if (Masks == null || Masks.Length == 0)
				throw new ArgumentException("No valid masks");

			// parallel factor
			if (args.Length > 2)
				uint.TryParse(args[2].Trim(), out ParallelFactor);

			// grab the known listfile
			ListfileHandler = new ListfileHandler();
			if (ListfileHandler.GetKnownListfile() || File.Exists("listfile.txt"))
			{
				Words = File.ReadAllLines("listfile.txt").SelectMany(x => x.Split(new char[] { '_', '/', ' ', '-', '.' })).Distinct().ToArray();
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
			Console.WriteLine($"Starting Wordlist ");

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

			int wildcardcount = mask.Count(x => x == '%');
			if (wildcardcount > 1)
			{
				Console.WriteLine($"Error: Templates must contain exactly one '%' character. `{mask}`");
				return;
			}
			else if (wildcardcount == 0)
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
					JenkinsHash j = new JenkinsHash();
					if (Array.BinarySearch(TargetHashes, j.ComputeHash(temp)) > -1)
						ResultStrings.Enqueue(temp);
				});
			}
			else
			{
				JenkinsHash j = new JenkinsHash();				
				var found = Words.Select(x => Normalise(mask.Replace("%", x))).Where(x => Array.BinarySearch(TargetHashes, j.ComputeHash(x)) > -1);
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
				hashes = hashes.Distinct().OrderBy(x => x);

				TargetHashes = hashes.ToArray();
			}

			if (TargetHashes == null || TargetHashes.Length < 1)
				throw new ArgumentException("Unknown listfile is missing or empty");
		}

		#endregion

		#region Helpers
		private string Normalise(string s) => s.Trim().Replace("/", "\\").ToUpperInvariant();
		#endregion
	}
}