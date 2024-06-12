using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

const int PageSize = 4096;

var input = args.FirstOrDefault("16").Trim();
if (!int.TryParse(input, out var length) || length <= 0)
{
    Console.WriteLine("Could not parse the length argument. Please provide a valid integer.");
    return;
}

var features = new[]
{
    Feat(Aes.IsSupported), Feat(Avx.IsSupported),
    Feat(Avx2.IsSupported), /* Feat(AvxVnni.IsSupported), */
    Feat(Bmi1.IsSupported), Feat(Bmi2.IsSupported),
    Feat(Fma.IsSupported), Feat(Lzcnt.IsSupported),
    Feat(Pclmulqdq.IsSupported), Feat(Popcnt.IsSupported),
    Feat(Sse.IsSupported), Feat(Sse2.IsSupported),
    Feat(Sse3.IsSupported), Feat(Ssse3.IsSupported),
    Feat(Sse41.IsSupported), Feat(Sse42.IsSupported),
};

Console.WriteLine($"""
    --- Environment Information ---
    Supported: {string.Join(", ", features.Where(f => f.IsSupported).Select(f => f.Name))}
    Unsupported: {string.Join(", ", features.Where(f => !f.IsSupported).Select(f => f.Name))}

    """);

Console.WriteLine("Verifying correctness of the benchmarks...");

Verify(ToAsciiUpper128);
Verify(ToAsciiUpper128x2);
if (!Avx2.IsSupported)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(
        "Warning: V256 is not accelerated on this system and will be unrolled to V128x2 by compiler.");
    Console.ResetColor();
}
Verify(ToAsciiUpper256);

Console.WriteLine($"Allocating {length} MiB of memory and filling it with random data...");

checked { length *= 1024 * 1024; }
var source = Alloc(length);
var sink = Alloc(length);

var choices = Enumerable
    .Range(0, 256)
    .Select(i => (byte)i)
    .ToArray();

Random.Shared.GetItems(choices, source);

Console.WriteLine("Memory allocation and initialization completed.");
Console.WriteLine("Running benchmarks...");
Console.WriteLine();

Execute(ToAsciiUpper128, ref source[0], ref sink[0], (nuint)length);
Execute(ToAsciiUpper128x2, ref source[0], ref sink[0], (nuint)length);
Execute(ToAsciiUpper256, ref source[0], ref sink[0], (nuint)length);

Free(source);
Free(sink);

Console.WriteLine("Benchmarks completed successfully.");

static (string Name, bool IsSupported) Feat(
    bool isSupported,
    [CallerArgumentExpression(nameof(isSupported))]
    string name = "")
{
    return (name[..name.IndexOf('.')], isSupported);
}

static void Verify(
    Benchmark bench, [CallerArgumentExpression(nameof(bench))] string name = "")
{
    var source = "Привіт, Всесвіт! Hello, World! Привіт, Всесвіт! Hello, World!"u8;
    var expected = "Привіт, Всесвіт! HELLO, WORLD! Привіт, Всесвіт! HELLO, WORLD!"u8;
    var actual = (stackalloc byte[source.Length]);

    bench(ref Unsafe.AsRef(in source[0]), ref actual[0], (nuint)source.Length);

    Assert(
        expected.SequenceEqual(actual),
        expr: $"{name} failed to convert the string to uppercase. Output: {Encoding.UTF8.GetString(actual)}");
}

static void Execute(
    Benchmark bench,
    ref byte src,
    ref byte dst,
    nuint length,
    [CallerArgumentExpression(nameof(bench))] string name = "")
{
    Console.WriteLine($"---- Executing {name} ----");
    Console.Write("Warming up...");
    for (int i = 0; i < 100; i++)
    {
        bench(ref src, ref dst, length);
    }
    Console.WriteLine("Done.");
    Console.WriteLine("Running...");

    var timings = new List<double>();
    for (int i = 0; i < 3000; i++)
    {
        var start = Stopwatch.GetTimestamp();
        bench(ref src, ref dst, length);
        var end = Stopwatch.GetElapsedTime(start);
        timings.Add(end.TotalMicroseconds);
    }

    var average = timings.Average();
    var throughput = (length / average * 1_000_000 / 1024 / 1024) * 2;

    Console.WriteLine($"Average execution time: {average:0.00} µs per iteration");
    Console.WriteLine($"Throughput: {throughput:0.00} MiB/s");
    Console.WriteLine($"---- Completed {name} ----{Environment.NewLine}");
}

[StackTraceHidden]
static void Assert(
    [DoesNotReturnIf(false)] bool condition,
    [CallerLineNumber] int line = 0,
    [CallerArgumentExpression(nameof(condition))] string expr = "")
{
    if (!condition) Throw(expr, line);

    [StackTraceHidden, DoesNotReturn]
    static void Throw(string expr, int line) => throw new($"\nAssertion failed at line {line}: {expr}");
}

static unsafe Span<byte> Alloc(int length) => new(NativeMemory.AlignedAlloc((nuint)length, PageSize), length);
static unsafe void Free(Span<byte> buffer) => NativeMemory.AlignedFree(Unsafe.AsPointer(ref buffer[0]));

[MethodImpl(MethodImplOptions.NoInlining)]
static void ToAsciiUpper128(ref byte src, ref byte dst, nuint length)
{
    Assert(Vector128.IsHardwareAccelerated);
    Assert(length >= (nuint)Vector128<byte>.Count);

    nuint offset = 0;
    var mask = Vector128.Create((sbyte)0x20);
    var overflow = Vector128.Create<sbyte>(128 - 'a');
    var bound = Vector128.Create<sbyte>(-127 + ('z' - 'a'));
    var lastvec = length - 16;
    do
    {
        var utf8 = Vector128.LoadUnsafe(ref src, offset).AsSByte();
        var changeCase = Vector128.LessThan(utf8 + overflow, bound) & mask;

        (utf8 ^ changeCase).AsByte().StoreUnsafe(ref dst, offset);

        offset += 16;
    } while (offset <= lastvec);

    var tail = Vector128.LoadUnsafe(ref src, lastvec).AsSByte();
    var tailChangeCase = Vector128.LessThan(tail.AsSByte() + overflow, bound) & mask;

    (tail ^ tailChangeCase).AsByte().StoreUnsafe(ref dst, lastvec);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void ToAsciiUpper128x2(ref byte src, ref byte dst, nuint length)
{
    Assert(Vector128.IsHardwareAccelerated);
    Assert(length >= (nuint)Vector128<byte>.Count * 2);

    nuint offset = 0;
    var mask = Vector128.Create((sbyte)0x20);
    var overflow = Vector128.Create<sbyte>(128 - 'a');
    var bound = Vector128.Create<sbyte>(-127 + ('z' - 'a'));
    var lastvec = length - 32;
    do
    {
        var utf8_1 = Vector128.LoadUnsafe(ref src, offset).AsSByte();
        var utf8_2 = Vector128.LoadUnsafe(ref src, offset + 16).AsSByte();
        var changeCase_1 = Vector128.LessThan(utf8_1 + overflow, bound) & mask;
        var changeCase_2 = Vector128.LessThan(utf8_2 + overflow, bound) & mask;

        (utf8_1 ^ changeCase_1).AsByte().StoreUnsafe(ref dst, offset);
        (utf8_2 ^ changeCase_2).AsByte().StoreUnsafe(ref dst, offset + 16);

        offset += 32;
    } while (offset <= lastvec);

    var tail_1 = Vector128.LoadUnsafe(ref src, lastvec).AsSByte();
    var tail_2 = Vector128.LoadUnsafe(ref src, lastvec + 16).AsSByte();
    var tailChangeCase_1 = Vector128.LessThan(tail_1.AsSByte() + overflow, bound) & mask;
    var tailChangeCase_2 = Vector128.LessThan(tail_2.AsSByte() + overflow, bound) & mask;

    (tail_1 ^ tailChangeCase_1).AsByte().StoreUnsafe(ref dst, lastvec);
    (tail_2 ^ tailChangeCase_2).AsByte().StoreUnsafe(ref dst, lastvec + 16);

}

[MethodImpl(MethodImplOptions.NoInlining)]
static void ToAsciiUpper256(ref byte src, ref byte dst, nuint length)
{
    Assert(length >= (nuint)Vector256<byte>.Count);

    nuint offset = 0;
    var mask = Vector256.Create((sbyte)0x20);
    var overflow = Vector256.Create<sbyte>(128 - 'a');
    var bound = Vector256.Create<sbyte>(-127 + ('z' - 'a'));
    var lastvec = length - 32;
    do
    {
        var utf8 = Vector256.LoadUnsafe(ref src, offset).AsSByte();
        var changeCase = Vector256.LessThan(utf8 + overflow, bound) & mask;

        (utf8 ^ changeCase).AsByte().StoreUnsafe(ref dst, offset);

        offset += 32;
    } while (offset <= lastvec);

    var tail = Vector256.LoadUnsafe(ref src, lastvec).AsSByte();
    var tailChangeCase = Vector256.LessThan(tail.AsSByte() + overflow, bound) & mask;

    (tail ^ tailChangeCase).AsByte().StoreUnsafe(ref dst, lastvec);
}

delegate void Benchmark(ref byte src, ref byte dst, nuint length);