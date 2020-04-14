using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CASCBruteforcer.Helpers
{
    class TableHash : HashAlgorithm
    {
        private uint hashValue;
		private static readonly byte[] hashBytes = new byte[0];

		static uint[] s_hashtable = new uint[] {
            0x486E26EE, 0xDCAA16B3, 0xE1918EEF, 0x202DAFDB,
            0x341C7DC7, 0x1C365303, 0x40EF2D37, 0x65FD5E49,
            0xD6057177, 0x904ECE93, 0x1C38024F, 0x98FD323B,
            0xE3061AE7, 0xA39B0FA1, 0x9797F25F, 0xE4444563,
        };

        public uint ComputeHash(string str)
        {
            byte[] data = Encoding.ASCII.GetBytes(str.Replace('/', '\\').ToUpperInvariant());
            ComputeHash(data);
            return hashValue;
        }

        public override void Initialize() { }

		protected override unsafe void HashCore(byte[] array, int ibStart, int cbSize)
		{
            uint v = 0x7FED7FED;
            uint x = 0xEEEEEEEE;
            byte c;

            for (int i = 0; i < array.Length; i++)
            {
                c = array[i];
                v += x;
                v ^= s_hashtable[(c >> 4) & 0xf] - s_hashtable[c & 0xf];
                x = x * 33 + v + c + 3;
            }

            hashValue = v;
        }

		protected override byte[] HashFinal() => hashBytes;
	}
}
