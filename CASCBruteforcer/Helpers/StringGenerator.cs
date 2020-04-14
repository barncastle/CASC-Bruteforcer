using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CASCBruteforcer.Algorithms
{
	class StringGenerator
	{
		static readonly char[] CharSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_- ".ToCharArray(); // 39 chars

		public static string Generate(char[] mask, ulong index, byte[] maskoffsets, uint charset, bool mirrored = false)
		{
			double nextchar = 1.0f / charset;
			ulong quotient = index;
			int increment = mirrored ? 2 : 1;

			for (int i = 0; i < maskoffsets.Length; i += increment)
			{
				mask[maskoffsets[i]] = CharSet[(uint)(quotient % charset)]; // maps to character in charset (result of %)
				if (mirrored)
					mask[maskoffsets[i + 1]] = mask[maskoffsets[i]]; // mirrored - copy the same char to the next offset too

				quotient = (ulong)(quotient * nextchar); // divide the number by its base to calculate the next character
			}

			return new string(mask);
		}
	}
}
