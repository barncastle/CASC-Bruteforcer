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

namespace CASCBruteforcer.Bruteforcers
{
	class Wordlist : IHash
	{
		const string CHECKFILES_URL = "https://bnet.marlam.in/checkFiles.php";

		private ListfileHandler ListfileHandler;
		private string[] Masks;
		private HashSet<ulong> TargetHashes;
		private string[] Words;
		private bool UseParallel;

		private HashSet<string> ResultStrings;

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

			// parallel flag
			UseParallel = (args.Length > 2 && args[2].Trim() == "1");

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
			ResultStrings = new HashSet<string>();
			ParseHashes();
		}

		public void Start()
		{
			for (int i = 0; i < Masks.Length; i++)
				Run(i);

			LogAndExport();
		}

		private void Run(int m)
		{
			string mask = Masks[m];

			if (mask.Count(x => x == '%') != 1)
			{
				Console.WriteLine($"Error: Templates must contain exactly one '%' character. `{mask}`");
				return;
			}


			// Start the work
			Console.WriteLine($"Starting Wordlist ");
			Stopwatch time = Stopwatch.StartNew();

			if (UseParallel)
			{
				Parallel.ForEach(Words, x =>
				{
					string temp = Normalise(mask.Replace("%", x));
					JenkinsHash j = new JenkinsHash();
					if (TargetHashes.Contains(j.ComputeHash(temp)))
						ResultStrings.Add(temp);
				});
			}
			else
			{
				JenkinsHash j = new JenkinsHash();
				var found = Words.Select(x => Normalise(mask.Replace("%", x))).Where(x => TargetHashes.Contains(j.ComputeHash(x)));
				if (found.Any())
					ResultStrings.UnionWith(found);
			}

			time.Stop();
			Console.WriteLine($"Completed in {time.Elapsed.TotalSeconds.ToString("0.00")} secs");
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
				IEnumerable<ulong> hashes = new ulong[1]; // 0 hash is used as a dump
#if DEBUG
				hashes = hashes.Concat(new ulong[] { 4097458660625243137, 13345699920692943597 }); // test hashes for the README examples
#endif
				hashes = hashes.Concat(lines.Where(x => ulong.TryParse(x.Trim(), NumberStyles.HexNumber, null, out dump)).Select(x => dump)); // hex
				hashes = hashes.Concat(lines.Where(x => ulong.TryParse(x.Trim(), out dump)).Select(x => dump)); // standard
				TargetHashes = new HashSet<ulong>(hashes);
			}

			if (TargetHashes == null || TargetHashes.Count <= 1)
				throw new ArgumentException("Unknown listfile is missing or empty");
		}

		#endregion

		#region Helpers
		private string Normalise(string s) => s.Trim().Replace("/", "\\").ToUpperInvariant();

		#endregion
	}
}