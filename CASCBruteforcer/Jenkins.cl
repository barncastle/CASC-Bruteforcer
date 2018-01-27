// Defines
#define HASHES_SIZE {HASHES_SIZE}
#define BUCKET_SIZE {BUCKET_SIZE} // max length of hashes grouped by first byte
#define DATA_SIZE {DATA_SIZE}
#define DATA_SIZE_MINUS (DATA_SIZE - 12)
#define HASH_PRIME (0xdeadbeef + {DATA_SIZE_REAL})
#define OFFSETS_SIZE {OFFSETS_SIZE}
#define NEXT_CHAR (1.0 / 39.0) // (1 / Charset.Length)

// Constants
constant int Offsets[OFFSETS_SIZE] = { {OFFSETS} };
constant char Charset[39] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_- ";
constant ulong HashLookup[HASHES_SIZE] = { {HASHES} }; // hashes to find, sorted by first byte
constant ushort HashOffsets[256] = { {HASH_OFFSETS} }; // offset of first hash with first byte in HashLookup


uint rotl(uint bits, uint amount) {
	return (bits << amount) | (bits >> (32 - amount));
}

ulong jenkins96(char *k) {
	uint a, b, c;
	a = b = c = HASH_PRIME;

	#ifdef opencl_unroll_hint
	__attribute__((opencl_unroll_hint))
	#endif
	for (short j = 0; j < DATA_SIZE_MINUS; j += 12)
	{
		a += (k[0 + j] + ((uint)k[1 + j] << 8) + ((uint)k[ 2 + j] << 16) + ((uint)k[ 3 + j] << 24));
		b += (k[4 + j] + ((uint)k[5 + j] << 8) + ((uint)k[ 6 + j] << 16) + ((uint)k[ 7 + j] << 24));
		c += (k[8 + j] + ((uint)k[9 + j] << 8) + ((uint)k[10 + j] << 16) + ((uint)k[11 + j] << 24));
		
		a -= c; a ^= rotl(c, 4); c += b;
		b -= a; b ^= rotl(a, 6); a += c;
		c -= b; c ^= rotl(b, 8); b += a;
		a -= c; a ^= rotl(c, 16); c += b;
		b -= a; b ^= rotl(a, 19); a += c;
		c -= b; c ^= rotl(b, 4); b += a;
	}

	a += (k[0 + DATA_SIZE_MINUS] + ((uint)k[1 + DATA_SIZE_MINUS] << 8) + ((uint)k[ 2 + DATA_SIZE_MINUS] << 16) + ((uint)k[ 3 + DATA_SIZE_MINUS] << 24));
	b += (k[4 + DATA_SIZE_MINUS] + ((uint)k[5 + DATA_SIZE_MINUS] << 8) + ((uint)k[ 6 + DATA_SIZE_MINUS] << 16) + ((uint)k[ 7 + DATA_SIZE_MINUS] << 24));
	c += (k[8 + DATA_SIZE_MINUS] + ((uint)k[9 + DATA_SIZE_MINUS] << 8) + ((uint)k[10 + DATA_SIZE_MINUS] << 16) + ((uint)k[11 + DATA_SIZE_MINUS] << 24));

	c ^= b; c -= rotl(b, 14);
	a ^= c; a -= rotl(c, 11);
	b ^= a; b -= rotl(a, 25);
	c ^= b; c -= rotl(b, 16);
	a ^= c; a -= rotl(c, 4);
	b ^= a; b -= rotl(a, 14);
	c ^= b; c -= rotl(b, 24);

	return ((ulong)c << 32) | b;
}


kernel void Bruteforce(uint loops, ulong offset, global ulong *result) {

	ulong index = get_global_id(0);
	const ulong size = get_global_size(0);
	char mask[DATA_SIZE] = {{DATA}}; // base string bytes

	ulong quotient, res;
	uint result_index;
	ushort hash_offset;

	#ifdef opencl_unroll_hint
	__attribute__((opencl_unroll_hint))
	#endif
	for(uint x = 0; x < loops; x++)
	{
		quotient = index + offset;

		#ifdef opencl_unroll_hint
		__attribute__((opencl_unroll_hint))
		#endif
		for(ulong i = 0; i < OFFSETS_SIZE; i++)
		{
			mask[Offsets[i]] = Charset[(uint)(quotient % 39)]; // maps to character in charset (result of %)
			quotient *= NEXT_CHAR; // divide the number by the base to calculate the next character (inverse multiplier is faster)
		}

		res = jenkins96(&mask);
		result_index = 0;
	
		// fills the appropiate result block (or result[0] if no match) with the matching index
		// starts at first hash of matching byte and runs for BUCKET_SIZE regardless to avoid branching		
		hash_offset = HashOffsets[(res & 0xFF)]; // calculate offset

		#ifdef opencl_unroll_hint
		__attribute__((opencl_unroll_hint))
		#endif
		for(ulong i = 0; i < BUCKET_SIZE; i++)
			result_index ^= (HashLookup[hash_offset + i] == res) * (hash_offset + i);

		result[result_index] = index;
		index += size;
	}
}


kernel void BruteforceMirrored(uint loops, ulong offset, global ulong *result) {

	ulong index = get_global_id(0);
	const ulong size = get_global_size(0);
	char mask[DATA_SIZE] = {{DATA}}; // base string bytes

	ulong quotient, res;
	uint result_index;
	ushort hash_offset;

	#ifdef opencl_unroll_hint
	__attribute__((opencl_unroll_hint))
	#endif
	for(uint x = 0; x < loops; x++)
	{
		quotient = index + offset;

		#ifdef opencl_unroll_hint
		__attribute__((opencl_unroll_hint))
		#endif
		for(ulong i = 0; i < OFFSETS_SIZE; i += 2)
		{
			mask[Offsets[i]] = mask[Offsets[i + 1]] = Charset[(uint)(quotient % 39)]; // maps to character in charset (result of %)
			quotient *= NEXT_CHAR; // divide the number by the base to calculate the next character (inverse multiplier is faster)
		}

		res = jenkins96(&mask);
		result_index = 0;
	
		// fills the appropiate result block (or result[0] if no match) with the matching index
		// starts at first hash of matching byte and runs for BUCKET_SIZE regardless to avoid branching		
		hash_offset = HashOffsets[(res & 0xFF)]; // calculate offset

		#ifdef opencl_unroll_hint
		__attribute__((opencl_unroll_hint))
		#endif
		for(ulong i = 0; i < BUCKET_SIZE; i++)
			result_index ^= (HashLookup[hash_offset + i] == res) * (hash_offset + i);

		result[result_index] = index;
		index += size;
	}
}