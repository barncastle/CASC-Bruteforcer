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
		const double NEXT_CHAR = (1.0 / 39.0);

		public static string Generate(char[] mask, ulong index, byte[] maskoffsets, bool mirrored)
		{
			ulong quotient = index;
			int increment = mirrored ? 2 : 1;

			for (int i = 0; i < maskoffsets.Length; i += increment)
			{
				mask[maskoffsets[i]] = CharSet[(uint)(quotient % 39)]; // maps to character in charset (result of %)
				if (mirrored)
					mask[maskoffsets[i + 1]] = mask[maskoffsets[i]]; // mirrored - copy the same char to the next offset too

				quotient = (ulong)(quotient * NEXT_CHAR); // divide the number by its base to calculate the next character
			}

			return new string(mask);
		}
	}
}
