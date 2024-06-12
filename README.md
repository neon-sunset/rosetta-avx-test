# rosetta-avx-test

Building requirements:
- .NET 8 SDK (either https://dot.net/download or `brew install dotnet-sdk`, *do not* mix installation methods)
- xcode tools

Make options:
- `make sse`: builds x86-x64-v2 version and places it in the `publish/sse` directory
- `make avx`: builds x86-x64-v2 + AVX2 version and places it in the `publish/avx` directory
- `make skylake`: builds x86-x64-v2, AVX2, BMI1, BMI2, LCZNT, MOVBE and FMA version and places it in the `publish/skylake` directory
- `make all`: builds all versions and places them in their respective directories

Running:
- run the executable from terminal
- optionally specify the buffer size in MiBs as the first argument (default is 16 MiB)

Notes:
This benchmark mostly verifies that the basics of directly mappable INT operations are working correctly and translated to 128x2 unrolling by Rosetta 2.  
The code itself is completely memory-bound on M-series but changing the buffer size may uncover issues with the way AVX2 behaves on new Rosetta 2 update (there should not be any).