using System;
using System.Collections.Generic;
using CASCBruteforcer.Bruteforcers;
using OpenCLlib;

namespace CASCBruteforcer
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("Hashing devices found ::");
			foreach (var AcceleratorDevice in AcceleratorDevice.All)
				Console.WriteLine($"  {AcceleratorDevice}");

			Console.WriteLine("");

			if (args == null || args.Length == 0)
				throw new ArgumentException("No arguments supplied");

			IHash hash;
			switch (args[0].ToLowerInvariant())
			{
				case "salsa":
					hash = new Salsa20();
					break;
				case "jenkins":
					hash = new Jenkins96();
					break;
				case "benchmark":
					BenchmarkJenkins(args);
					return;
				case "wordlist":
					hash = new Wordlist();
					break;
				default:
					throw new ArgumentException("Invalid hash type");
			}

			// override console exit event
			CleanExitHandler.Attach();
			
			hash.LoadParameters(args);
			hash.Start();
		}

		static void BenchmarkJenkins(params string[] args)
		{
			int perms = 5;
			if (args != null && args.Length > 1 && !int.TryParse(args[1], out perms))
				perms = 5;

			perms = Math.Min(Math.Max(perms, 1), 9); // clamp to 1 - 9

			char[] filename = "interface/cinematics/legion_dh2.mp3".ToCharArray(); // build the dummy template
			for (int i = 0; i < perms; i++)
				filename[30 - i] = '%';

			Console.WriteLine($"Benchmarking Jenkins @ {perms} permutations (39^{perms}) :: ");
			
			List<string> types = new List<string>() { "gpu", "cpu", "all" }; // calculate the available devices
			if (!AcceleratorDevice.HasCPU)
				types.Remove("cpu");
			if (!AcceleratorDevice.HasGPU)
				types.Remove("gpu");
			if (types.Count == 2 && AcceleratorDevice.All.Length == 1)
				types.Remove("all");

			Jenkins96 jenkins = new Jenkins96();
			foreach (var type in types)
			{
				jenkins.LoadTestParameters(type, new string(filename));
				Console.WriteLine("");
				Console.WriteLine($"-- {type.ToUpper()} Test --");
				jenkins.Start();
			}

			Console.ReadLine();
		}
	}
}
