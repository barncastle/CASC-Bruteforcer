// Defines
#define HASHES_SIZE {HASHES_SIZE}
#define BUCKET_SIZE {BUCKET_SIZE} // max length of hashes grouped by first byte
#define DATA_SIZE {DATA_SIZE_REAL}
#define OFFSETS_SIZE {OFFSETS_SIZE}
#define NEXT_CHAR (1.0 / 26.0) // (1 / Charset.Length)

// Constants
constant uint Offsets[OFFSETS_SIZE] = { {OFFSETS} };
constant char Charset[26] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
constant uint HashLookup[HASHES_SIZE] = { {HASHES} }; // hashes to find, sorted by first byte
constant ushort HashOffsets[256] = { {HASH_OFFSETS} }; // offset of first hash with first byte in HashLookup

constant uint s_hashtable[16] = {
 	0x486E26EE, 0xDCAA16B3, 0xE1918EEF, 0x202DAFDB,
 	0x341C7DC7, 0x1C365303, 0x40EF2D37, 0x65FD5E49,
 	0xD6057177, 0x904ECE93, 0x1C38024F, 0x98FD323B,
 	0xE3061AE7, 0xA39B0FA1, 0x9797F25F, 0xE4444563
};

uint hash(char *k) {
	uint v = 0x7FED7FED;
	uint x = 0xEEEEEEEE;
	for (short i = 0; i < DATA_SIZE; i++) {
		v += x;
		v ^= s_hashtable[(k[i] >> 4) & 0xf] - s_hashtable[k[i] & 0xf];
		x = x * 33 + v + k[i] + 3;
	}

	// Validatation	
	// - fills the appropiate result block (or result[0] if no match) with the matching index
	// - starts at first hash of matching byte and runs for BUCKET_SIZE regardless to avoid branching		
	uint result_index = 0;
	ushort hash_offset = HashOffsets[(v & 0xFF)]; // calculate offset

	#ifdef opencl_unroll_hint
	__attribute__((opencl_unroll_hint))
	#endif
	for(ulong i = 0; i < BUCKET_SIZE; i++)
		result_index ^= (HashLookup[hash_offset + i] == v) * (hash_offset + i);

	return result_index;
}

kernel void Bruteforce(ulong offset, global ulong *result) {

	const ulong id = get_global_id(0);
	const ulong index = id + offset;
	char mask[DATA_SIZE] = {{DATA}}; // base string bytes

	ulong quotient = index;

	#ifdef opencl_unroll_hint
	__attribute__((opencl_unroll_hint))
	#endif
	for(ulong i = 0; i < OFFSETS_SIZE; i++)
	{
		mask[Offsets[i]] = Charset[(uint)(quotient % 26)]; // maps to character in charset (result of %)
		quotient *= NEXT_CHAR; // divide the number by the base to calculate the next character (inverse multiplier is faster)
	}

	result[hash(&mask)] = index;
}