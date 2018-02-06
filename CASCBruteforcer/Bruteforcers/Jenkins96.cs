using CASCBruteforcer.Algorithms;
using CASCBruteforcer.Helpers;
using Cloo;
using OpenCLlib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CASCBruteforcer.Bruteforcers
{
	class Jenkins96 : IHash
	{
		const long BASE_GLOBAL_WORKSIZE = uint.MaxValue; // sizeof(size_t) usually uint

		const string CHECKFILES_URL = "https://bnet.marlam.in/checkFiles.php";

		private ListfileHandler ListfileHandler;
		private ComputeDeviceTypes ComputeDevice;
		private string[] Masks;
		private HashSet<ulong> TargetHashes;
		private bool IsBenchmark = false;
		private bool IsMirrored = false;

		private Queue<ulong> ResultQueue;
		private HashSet<string> ResultStrings;

		public void LoadParameters(params string[] args)
		{
			if (args.Length < 3)
				throw new ArgumentException("Incorrect number of arguments");

			// what device to use
			switch (args[1].ToLower().Trim())
			{
				case "gpu":
					ComputeDevice = ComputeDeviceTypes.Gpu;
					break;
				case "cpu":
					ComputeDevice = ComputeDeviceTypes.Cpu;
					break;
				default:
					ComputeDevice = ComputeDeviceTypes.All;
					break;
			}

			// format + validate template masks
			if (File.Exists(args[2]))
			{
				Masks = File.ReadAllLines(args[2]).Select(x => Normalise(x)).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
			}
			else if (!string.IsNullOrWhiteSpace(args[2]))
			{
				Masks = new string[] { Normalise(args[2]) };
			}

			if (Masks == null || Masks.Length == 0)
				throw new ArgumentException("No valid masks");

			// check for mirrored flag
			IsMirrored = (args.Length > 3 && args[3].Trim() == "1");

			// grab any listfile filters
			string product = args.Length > 4 ? args[4] : "";
			string exclusions = args.Length > 5 ? args[5] : "";
			ListfileHandler = new ListfileHandler(product, exclusions);

			ResultQueue = new Queue<ulong>();
			ResultStrings = new HashSet<string>();
			IsBenchmark = false;
		}

		public void LoadTestParameters(string device, string mask)
		{
			// what device to use
			switch (device)
			{
				case "gpu":
					ComputeDevice = ComputeDeviceTypes.Gpu;
					break;
				case "cpu":
					ComputeDevice = ComputeDeviceTypes.Cpu;
					break;
				default:
					ComputeDevice = ComputeDeviceTypes.All;
					break;
			}

			Masks = new string[] { Normalise(mask) };

			ListfileHandler = new ListfileHandler();
			ResultQueue = new Queue<ulong>();
			ResultStrings = new HashSet<string>();
			IsBenchmark = true;
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
			ParseHashes(mask);

			// handle templates without wildcards
			if (!mask.Contains('%'))
			{
				ResultQueue.Enqueue(0);
				Validate(mask, new byte[0]);
				return;
			}

			// resize mask to next % 12 for faster jenkins
			byte[] maskdata = Encoding.ASCII.GetBytes(mask);
			Array.Resize(ref maskdata, (mask.Length + (12 - mask.Length % 12) % 12));

			// calculate the indicies of the wildcard chars
			byte[] maskoffsets = Enumerable.Range(0, mask.Length).Where(i => mask[i] == '%').Select(i => (byte)i).ToArray();
			if (maskoffsets.Length > 12 * (IsMirrored ? 2 : 1))
			{
				Console.WriteLine($"Error: Too many wildcards - maximum is {12 * (IsMirrored ? 2 : 1)}. `{mask}`");
				return;
			}

			// mirrored is two indentical masks so must have an even count of wildcards
			if (IsMirrored && maskoffsets.Length % 2 != 0)
			{
				Console.WriteLine($"Error: Mirrored flag used with an odd number of wildcards. `{mask}`");
				return;
			}

			// reorder mirrored indices for faster permutation computing
			if (IsMirrored)
			{
				int halfcount = maskoffsets.Length / 2;
				byte[] temp = new byte[maskoffsets.Length];
				for (int i = 0; i < halfcount; i++)
				{
					temp[i * 2] = maskoffsets[i];
					temp[(i * 2) + 1] = maskoffsets[halfcount + i];
				}
				maskoffsets = temp;
			}

			// replace kernel placeholders - faster than using buffers
			KernelWriter kernel = new KernelWriter(Properties.Resources.Jenkins);
			kernel.ReplaceArray("DATA", maskdata);
			kernel.ReplaceArray("OFFSETS", maskoffsets);
			kernel.ReplaceArray("HASHES", TargetHashes.Select(x => x + "ULL")); // prefix ULL to remove the warnings..
			kernel.Replace("DATA_SIZE_REAL", mask.Length);
			kernel.ReplaceOffsetArray(TargetHashes);

			// load CL - filter contexts to the specific device type
			MultiCL cl = new MultiCL(ComputeDevice);
			Console.WriteLine($"Loading kernel - {TargetHashes.Count - 1} hashes. This may take a minute...");
			cl.SetKernel(kernel.ToString(), IsMirrored ? "BruteforceMirrored" : "Bruteforce");

			// performance settings
			// - get the warp size
			// - align the threads to the warp
			// - calculate the smallest local size !Note: GLOBAL % LOCAL must be 0 for NVidia
			long WARP_SIZE = cl.WarpSize;
			long LOCAL_WORKSIZE = cl.MaxLocalSize;
			long GLOBAL_WORKSIZE = cl.CalculateGlobalsize(BASE_GLOBAL_WORKSIZE, LOCAL_WORKSIZE);			

			// limit workload to MAX_WORKSIZE and use an on-device loop to breach that value
			BigInteger combinations = BigInteger.Pow(39, maskoffsets.Length / (IsMirrored ? 2 : 1)); // total combinations
			uint loops = (uint)Math.Floor(Math.Exp(BigInteger.Log(combinations) - BigInteger.Log(GLOBAL_WORKSIZE)));

			// Start the work
			Console.WriteLine($"Starting Jenkins Hashing :: {combinations} combinations ");
			Stopwatch time = Stopwatch.StartNew();

			// output buffer arg
			int bufferSize = (TargetHashes.Count + (8 - TargetHashes.Count % 8) % 8); // buffer should be 64 byte aligned
			var resultArg = CLArgument<ulong>.CreateReturn(bufferSize);

			// set up internal loop of GLOBAL_WORKSIZE
			if (loops > 0)
			{
				for (uint i = 0; i < loops; i++)
				{
					// index offset, count, output buffer
					cl.SetParameter((ulong)(i * GLOBAL_WORKSIZE), GLOBAL_WORKSIZE, resultArg);

					// my card crashes if it is going full throttle and I forcibly exit the kernel
					// this overrides the default exit behaviour and waits for a break in GPU processing before exiting
					// - if the exit event is fired twice it'll just force close
					CleanExitHandler.IsProcessing = ComputeDevice.HasFlag(ComputeDeviceTypes.Gpu);
					Enqueue(cl.InvokeReturn<ulong>(GLOBAL_WORKSIZE, LOCAL_WORKSIZE, bufferSize));
					CleanExitHandler.ProcessExit();

					if (i == 0)
						LogEstimation(loops, time.Elapsed.TotalSeconds);
				}

				combinations -= loops * GLOBAL_WORKSIZE;
			}

			// process remaining
			if (combinations > 0)
			{
				ulong offset = (ulong)(loops * GLOBAL_WORKSIZE);

				// recalculate sizes for the remainder
				LOCAL_WORKSIZE = cl.CalculateLocalsize((long)combinations, cl.MaxLocalSize);

				// index offset, count, output buffer
				cl.SetParameter((ulong)(loops * GLOBAL_WORKSIZE), GLOBAL_WORKSIZE,  resultArg);							

				CleanExitHandler.IsProcessing = ComputeDevice.HasFlag(ComputeDeviceTypes.Gpu);
				Enqueue(cl.InvokeReturn<ulong>((long)combinations, LOCAL_WORKSIZE, bufferSize));
				CleanExitHandler.ProcessExit();
			}

			time.Stop();
			Console.WriteLine($"Completed in {time.Elapsed.TotalSeconds.ToString("0.00")} secs");
			Validate(mask, maskoffsets);

			CleanExitHandler.IsProcessing = false;
		}


		#region Validation
		private void LogEstimation(uint loops, double seconds)
		{
			Task.Run(() =>
			{
				DateTime estimate = DateTime.Now;
				for (int x = 0; x < loops; x++)
					estimate = estimate.AddSeconds(seconds);
				Console.WriteLine($" Estimated Completion {estimate}");
			});
		}

		private void Enqueue(ulong[] results)
		{
			// dump everything into a collection and deal with it later
			foreach (var r in results)
				if (r != 0)
					ResultQueue.Enqueue(r);
		}

		private void Validate(string mask, byte[] maskoffsets)
		{
			char[] maskdata = mask.ToCharArray();

			// sanity check the results
			var j = new JenkinsHash();
			while (ResultQueue.Count > 0)
			{
				string s = StringGenerator.Generate(maskdata, ResultQueue.Dequeue(), maskoffsets, IsMirrored);
				ulong h = j.ComputeHash(s);
				if (TargetHashes.Contains(h))
					ResultStrings.Add(s);
			}
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
			// log completion
			Console.WriteLine($"Found {ResultStrings.Count}:");

			if (ResultStrings.Count > 0)
			{
				// print to the screen
				foreach (var r in ResultStrings)
					Console.WriteLine($"  {r.Replace("\\", "/").ToLower()}");

				if (!IsBenchmark)
				{
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
			}

			Console.WriteLine("");
		}

		#endregion

		#region Unknown Hash Functions
		private void ParseHashes(string mask)
		{
			bool parseListfile = ListfileHandler.GetUnknownListfile("unk_listfile.txt", mask);

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
			hashes = hashes.OrderBy(HashSort); // order by first byte - IMPORTANT

			TargetHashes = new HashSet<ulong>(hashes);

			if (TargetHashes == null || TargetHashes.Count <= 1)
				throw new ArgumentException("Unknown listfile is missing or empty");
		}

		#endregion

		#region Helpers
		private string Normalise(string s) => s.Trim().Replace("/", "\\").ToUpperInvariant();

		public static Func<ulong, ulong> HashSort = (x) => x & 0xFF;
		#endregion
	}
}
