sse:
	dotnet publish \
		-c Release \
		-r osx-x64 \
		-o publish/sse \
		-p:IlcInstructionSet=x86-x64-v2 \
		-p:AssemblyName=rosetta-test-sse

avx:
	dotnet publish \
		-c Release \
		-r osx-x64 \
		-o publish/avx \
		-p:AssemblyName=rosetta-test-avx

skylake:
	dotnet publish \
		-c Release \
		-r osx-x64 \
		-o publish/skylake \
		-p:IlcInstructionSet=x86-x64-v3 \
		-p:AssemblyName=rosetta-test-skylake

all: sse avx skylake

clean:
	rm -rf publish && \
	dotnet clean -c Release