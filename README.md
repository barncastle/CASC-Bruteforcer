# CASC Bruteforcer

A GPU accelerated Salsa20 and Jenkins96 bruteforce tool. Benchmarks have been tested against an AMD Radeon R9 380.

A big thanks to [Marlamin](https://github.com/Marlamin) for hacking his site around to accommodate this project.

## Salsa20
The salsa implementation is designed to be as small and fast as possible, by only hashing a 4 character magic, at the cost of more collisions.

#### Arguments
1. Mode (string) - salsa
2. Encrypted Magic (4 char string) - First 4 bytes of the encrypted file as a string
3. Expected Magic (4 char string) - The decrypted 4 byte magic
4. IV (16 char hex string)
5. Key Offset Part 1 (ulong) - Offset of the first 8 bytes of the key
6. Key Offset Part 2 (ulong) - Offset of the second 8 bytes of the key
7. Increment Mode (byte flag) - Increment 1 = Arg #5, 2 = Arg #6, 3 = Both

#### Example
To bruteforce a CDN key via it's build config starting with a key of 0 (16 byte number) and only increment the first 8 bytes ([source](https://wowdev.wiki/CASC#Armadillo)).
>cascbruteforcer "salsa" "ó…«" "# Bu" "2ac8b83058af891d" 0 0 1

#### Benchmarks
On my hardware I average 4000 million hashes a second.


## Jenkins96
The jenkins implementation uses index based permutations and a filename template to attempt to match unnamed root file name hashes ([source](https://wowdev.wiki/CASC#Root)). The unnamed hash list is automatically pulled from [bnet.marlam.in](https://bnet.marlam.in) and is filtered based on the template used. Found names will be exported upon completion to a file named 'Output.txt'. 
Note: The `template` argument can also be a .txt file containing multiple wildcard strings.

For a list of common filename structures [see here](https://wowdev.wiki/Filename_Structures).

#### Arguments
1. Mode (string) - "jenkins"
2. Device (string) - "gpu", "cpu" or "all"
3. Template (string) -  a file path to a list of templates or a single template using `%` as wildcard characters
4. Mirrored (boolean) - (optional) if set to 1 the wildcard characters will be evenly applied twice
5. Product (string) - (optional) "wow", "wowt" or "wow_beta", filters the unnamed hashes to a specific build
5. Excluded Filetypes (string) - (optional) excludes specific [filetypes](https://bnet.marlam.in/filestats.php) from the unknown hashes

#### Examples
To test for unknown 5 character mp3s in the `interface/cinematics/` directory:
>cascbruteforcer "jenkins" "gpu" "interface/cinematics/%%%%%.mp3"

To find `world/maps/gilneas/gilneas.tex` using a mirrored template:
>cascbruteforcer "jenkins" "gpu" "world/maps/g%%%%%s/g%%%%%s.tex" 1

#### Benchmarks
On my GPU I can compare all 7 character (39^7) permutations against ~5500 unnamed hashes in 50 seconds averaging ~2700 million hashes a second.

Depending on your hardware you may get better jenkins performance using a different combination of kernels. To test this use "benchmark" and a number of wildcard characters between 1 and 9 e.g.
>cascbruteforcer benchmark 5

## Wordlist
A very simple wordlist tester. The wordlist is generated from splitting all [known filenames](https://github.com/bloerwald/wow-listfile) by common delimiters. 
Note: The `template` argument can also be a .txt file containing multiple wildcard strings.

For a list of common filename structures [see here](https://wowdev.wiki/Filename_Structures).

#### Arguments
1. Template (string) -  a file path to a list of templates or a single template using a `%` as the wildcard character
2. Parallel (int) - (optional) defines the maximum degree of parallelism or 0 for none
3. Wordlist (string) - (optional) file path to a custom wordlist to use instead of the known filenames

#### Examples
To find `world/maps/gilneas/gilneas.tex`:
>cascbruteforcer "wordlist" "world/maps/gilneas/%.tex"
