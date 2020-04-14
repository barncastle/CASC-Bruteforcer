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
    class DBTableHash : IHash
	{
		private long BASE_GLOBAL_WORKSIZE = uint.MaxValue - 63; // sizeof(size_t) usually uint, aligned to % 64 warp

		private ComputeDeviceTypes ComputeDevice;
		private string[] Masks;
		private HashSet<uint> TargetHashes;
		private bool IsBenchmark = false;

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
				Masks = File.ReadAllLines(args[2])
							.Select(x => Normalise(x))
							.Where(x => !string.IsNullOrWhiteSpace(x))
							.OrderBy(x => Path.GetExtension(x))
							.ToArray();
			}
			else if (!string.IsNullOrWhiteSpace(args[2]))
			{
				Masks = new string[] { Normalise(args[2]) };
			}

			if (Masks == null || Masks.Length == 0)
				throw new ArgumentException("No valid masks");

			if (args.Length > 2)
				ParseHashes(args[3]);
			
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

			// handle templates without wildcards
			if (!mask.Contains('%'))
			{
				ResultQueue.Enqueue(0);
				Validate(mask, new byte[0]);
				return;
			}

			byte[] maskdata = Encoding.ASCII.GetBytes(mask);

			// calculate the indicies of the wildcard chars
			byte[] maskoffsets = Enumerable.Range(0, mask.Length).Where(i => mask[i] == '%').Select(i => (byte)i).ToArray();
			if (maskoffsets.Length > 12)
			{
				Console.WriteLine($"Error: Too many wildcards - maximum is {12}. `{mask}`");
				return;
			}

			// replace kernel placeholders - faster than using buffers
			KernelWriter kernel = new KernelWriter(Properties.Resources.Table);
			kernel.ReplaceArray("DATA", maskdata);
			kernel.ReplaceArray("OFFSETS", maskoffsets);
			kernel.Replace("DATA_SIZE_REAL", mask.Length);
			kernel.ReplaceOffsetArray(TargetHashes);

			// load CL - filter contexts to the specific device type
			MultiCL cl = new MultiCL(ComputeDevice);
			Console.WriteLine($"Loading kernel - {TargetHashes.Count - 1} hashes");
			cl.SetKernel(kernel.ToString(), "Bruteforce");

			// output buffer arg
			int bufferSize = (TargetHashes.Count + (8 - TargetHashes.Count % 8) % 8); // buffer should be 64 byte aligned
			var resultArg = CLArgument<ulong>.CreateReturn(bufferSize);

			// alignment calculations			
			BigInteger COMBINATIONS = BigInteger.Pow(26, maskoffsets.Length); // calculate combinations
			long GLOBAL_WORKSIZE = 0, LOOPS = 0;
			long WARP_SIZE = cl.WarpSize;

			// Start the work
			Console.WriteLine($"Starting DB Table Hashing :: {COMBINATIONS} combinations ");
			Stopwatch time = Stopwatch.StartNew();

			COMBINATIONS = AlignTo(COMBINATIONS, WARP_SIZE); // align to the warp size

			// nvidia + too many threads + lots of wildcards = out of resources
			if (cl.HasNVidia && maskoffsets.Length > 7)
				BASE_GLOBAL_WORKSIZE -= (maskoffsets.Length - 7) * 613566752;

			if (COMBINATIONS > BASE_GLOBAL_WORKSIZE)
			{
				GLOBAL_WORKSIZE = (long)ReduceTo(BASE_GLOBAL_WORKSIZE, WARP_SIZE);
				LOOPS = (uint)Math.Floor(Math.Exp(BigInteger.Log(COMBINATIONS) - BigInteger.Log(GLOBAL_WORKSIZE)));

				// set up internal loop of GLOBAL_WORKSIZE
				for (uint i = 0; i < LOOPS; i++)
				{
					// index offset, count, output buffer
					cl.SetParameter((ulong)(i * GLOBAL_WORKSIZE), resultArg);

					// my card crashes if it is going full throttle and I forcibly exit the kernel
					// this overrides the default exit behaviour and waits for a break in GPU processing before exiting
					// - if the exit event is fired twice it'll just force close
					CleanExitHandler.IsProcessing = ComputeDevice.HasFlag(ComputeDeviceTypes.Gpu);
					Enqueue(cl.InvokeReturn<ulong>(GLOBAL_WORKSIZE, null, bufferSize));
					CleanExitHandler.ProcessExit();

					if (i == 0)
						LogEstimation(LOOPS, time.Elapsed.TotalSeconds);

					COMBINATIONS -= GLOBAL_WORKSIZE;
				}
			}

			if (COMBINATIONS > 0)
			{
				// index offset, count, output buffer
				cl.SetParameter((ulong)(LOOPS * GLOBAL_WORKSIZE), resultArg);

				GLOBAL_WORKSIZE = (long)AlignTo(COMBINATIONS, WARP_SIZE);

				CleanExitHandler.IsProcessing = ComputeDevice.HasFlag(ComputeDeviceTypes.Gpu);
				Enqueue(cl.InvokeReturn<ulong>(GLOBAL_WORKSIZE, null, bufferSize));
				CleanExitHandler.ProcessExit();
			}

			time.Stop();
			Console.WriteLine($"Completed in {time.Elapsed.TotalSeconds.ToString("0.00")} secs");
			Validate(mask, maskoffsets);

			CleanExitHandler.IsProcessing = false;
		}

		private BigInteger AlignTo(BigInteger source, BigInteger factor) => (source + (factor - source % factor) % factor);
		private BigInteger ReduceTo(BigInteger source, BigInteger factor) => source - (source % factor);


		#region Validation
		private void LogEstimation(long loops, double seconds)
		{
			Task.Run(() =>
			{
				DateTime estimate = DateTime.Now;
				for (uint x = 0; x < loops; x++)
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
			var j = new TableHash();
			while (ResultQueue.Count > 0)
			{
				string s = StringGenerator.Generate(maskdata, ResultQueue.Dequeue(), maskoffsets, 26);
				uint h = j.ComputeHash(s);
				if (TargetHashes.Contains(h))
					ResultStrings.Add(s);
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
				}
			}

			Console.WriteLine("");
		}

		#endregion

		#region Unknown Hash Functions
		private void ParseHashes(string mask)
		{
			var lines = mask.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

			// parse items - hex and standard because why not
			uint dump = 0;
			IEnumerable<uint> hashes = new uint[1]; // 0 hash is used as a dump
#if DEBUG
			hashes = hashes.Concat(new uint[] { 2442913102 }); // test hashes for the README examples
#endif
			hashes = hashes.Concat(lines.Where(x => uint.TryParse(x.Trim(), NumberStyles.HexNumber, null, out dump)).Select(x => dump)); // hex
			hashes = hashes.Concat(lines.Where(x => uint.TryParse(x.Trim(), out dump)).Select(x => dump)); // standard
			hashes = hashes.Distinct().OrderBy(HashSort); // order by first byte - IMPORTANT

			TargetHashes = new HashSet<uint>(hashes);

			if (TargetHashes == null || TargetHashes.Count <= 1)
				throw new ArgumentException("Unknown listfile is missing or empty");
		}

		#endregion

		#region Helpers
		private string Normalise(string s) => s.Trim().Replace("/", "\\").ToUpperInvariant();

		public static Func<uint, uint> HashSort = (x) => x & 0xFF;
		#endregion
	}
}
