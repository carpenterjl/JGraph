using System.Collections.Concurrent;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace JGraph.NativeHost;

/// <summary>
/// Calls a function pointer through a signature of <see cref="Slot"/>s. Each distinct signature,
/// after structs are lowered, compiles once to a <see cref="DynamicMethod"/> that loads the arguments
/// from an array of 8-byte cells and emits an unmanaged <c>calli</c>; the JIT then places every
/// argument by the x64 convention. x64 has one convention, so <c>__stdcall</c> and <c>__cdecl</c> are
/// the same call, as the C compiler treats them.
/// </summary>
/// <remarks>
/// A struct by value follows the x64 rule without a generated type: 1, 2, 4 or 8 bytes travel as an
/// unsigned integer of that width, anything else as a pointer to a copy the callee may change. A
/// struct return of those four sizes comes back in the integer register; any other is written through
/// a hidden first argument pointing at a buffer the caller owns, and the function answers that pointer.
/// </remarks>
public static unsafe class CallCompiler
{
    private delegate void Invoker(nint function, long* arguments, long* result);

    private static readonly ConcurrentDictionary<string, Invoker> Compiled = new(StringComparer.Ordinal);

    /// <summary>How many signatures have been compiled, for the tests of the cache.</summary>
    public static int CompiledCount => Compiled.Count;

    /// <summary>
    /// Calls <paramref name="function"/> with <paramref name="arguments"/>' bytes laid out as the
    /// request carries them (<see cref="Slot.WireSize"/> each) and answers the return's bytes
    /// (<see cref="Slot.ReturnSize"/>).
    /// </summary>
    public static byte[] Call(nint function, Slot result, IReadOnlyList<Slot> parameters, ReadOnlySpan<byte> arguments)
    {
        bool hiddenReturn = result.Kind == SlotKind.Struct && !InRegister(result.Size);
        int count = parameters.Count + (hiddenReturn ? 1 : 0);
        var lowered = new SlotKind[count];
        long* cells = stackalloc long[Math.Max(count, 1)];
        var copies = new List<nint>();
        nint returnBuffer = 0;
        try
        {
            int cell = 0;
            if (hiddenReturn)
            {
                returnBuffer = (nint)NativeMemory.AllocZeroed((nuint)result.Size);
                copies.Add(returnBuffer);
                lowered[cell] = SlotKind.Pointer;
                cells[cell++] = returnBuffer;
            }

            int offset = 0;
            foreach (Slot parameter in parameters)
            {
                ReadOnlySpan<byte> bytes = arguments.Slice(offset, parameter.WireSize);
                offset += parameter.WireSize;
                if (parameter.Kind != SlotKind.Struct)
                {
                    lowered[cell] = parameter.Kind;
                    cells[cell++] = MemoryMarshal.Read<long>(bytes);
                }
                else if (InRegister(parameter.Size))
                {
                    long value = 0;
                    bytes[..parameter.Size].CopyTo(new Span<byte>(&value, 8));
                    lowered[cell] = RegisterKind(parameter.Size);
                    cells[cell++] = value;
                }
                else
                {
                    nint copy = (nint)NativeMemory.Alloc((nuint)Math.Max(parameter.Size, 1));
                    copies.Add(copy);
                    bytes[..parameter.Size].CopyTo(new Span<byte>((void*)copy, parameter.Size));
                    lowered[cell] = SlotKind.Pointer;
                    cells[cell++] = copy;
                }
            }

            SlotKind returns = hiddenReturn ? SlotKind.Pointer
                : result.Kind == SlotKind.Struct ? RegisterKind(result.Size)
                : result.Kind;

            long answer = 0;
            InvokerFor(returns, lowered)(function, cells, &answer);

            if (hiddenReturn)
            {
                return new ReadOnlySpan<byte>((void*)returnBuffer, result.Size).ToArray();
            }

            return result.Kind switch
            {
                SlotKind.Void => [],
                SlotKind.Struct => new ReadOnlySpan<byte>(&answer, result.Size).ToArray(),
                _ => new ReadOnlySpan<byte>(&answer, 8).ToArray(),
            };
        }
        finally
        {
            foreach (nint copy in copies)
            {
                NativeMemory.Free((void*)copy);
            }
        }
    }

    private static bool InRegister(int size) => size is 1 or 2 or 4 or 8;

    private static SlotKind RegisterKind(int size) => size switch
    {
        1 => SlotKind.UInt8,
        2 => SlotKind.UInt16,
        4 => SlotKind.UInt32,
        _ => SlotKind.UInt64,
    };

    private static Invoker InvokerFor(SlotKind returns, SlotKind[] parameters)
    {
        string key = string.Concat(((char)('A' + (int)returns)).ToString(), new string(Array.ConvertAll(parameters, static p => (char)('A' + (int)p))));
        return Compiled.GetOrAdd(key, _ => Compile(returns, parameters));
    }

    private static Invoker Compile(SlotKind returns, SlotKind[] parameters)
    {
        var method = new DynamicMethod(
            "native_call", typeof(void), [typeof(nint), typeof(long*), typeof(long*)], typeof(CallCompiler).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        if (returns != SlotKind.Void)
        {
            il.Emit(OpCodes.Ldarg_2);
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, i * 8);
            il.Emit(OpCodes.Add);
            il.Emit(parameters[i] switch
            {
                SlotKind.Int8 => OpCodes.Ldind_I1,
                SlotKind.UInt8 => OpCodes.Ldind_U1,
                SlotKind.Int16 => OpCodes.Ldind_I2,
                SlotKind.UInt16 => OpCodes.Ldind_U2,
                SlotKind.Int32 => OpCodes.Ldind_I4,
                SlotKind.UInt32 => OpCodes.Ldind_U4,
                SlotKind.Int64 or SlotKind.UInt64 => OpCodes.Ldind_I8,
                SlotKind.Single => OpCodes.Ldind_R4,
                SlotKind.Double => OpCodes.Ldind_R8,
                SlotKind.Pointer => OpCodes.Ldind_I,
                _ => throw new ArgumentException($"A {parameters[i]} slot cannot be an argument."),
            });
        }

        il.Emit(OpCodes.Ldarg_0);
        il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, ClrType(returns), Array.ConvertAll(parameters, ClrType));

        switch (returns)
        {
            case SlotKind.Void:
                break;
            case SlotKind.Int8 or SlotKind.Int16 or SlotKind.Int32:
                il.Emit(OpCodes.Conv_I8);
                il.Emit(OpCodes.Stind_I8);
                break;
            case SlotKind.UInt8 or SlotKind.UInt16 or SlotKind.UInt32:
                il.Emit(OpCodes.Conv_U8);
                il.Emit(OpCodes.Stind_I8);
                break;
            case SlotKind.Int64 or SlotKind.UInt64:
                il.Emit(OpCodes.Stind_I8);
                break;
            case SlotKind.Single:
                il.Emit(OpCodes.Stind_R4);
                break;
            case SlotKind.Double:
                il.Emit(OpCodes.Stind_R8);
                break;
            case SlotKind.Pointer:
                il.Emit(OpCodes.Stind_I);
                break;
            default:
                throw new ArgumentException($"A {returns} slot cannot be a return.");
        }

        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Invoker>();
    }

    private static Type ClrType(SlotKind kind) => kind switch
    {
        SlotKind.Void => typeof(void),
        SlotKind.Int8 => typeof(sbyte),
        SlotKind.UInt8 => typeof(byte),
        SlotKind.Int16 => typeof(short),
        SlotKind.UInt16 => typeof(ushort),
        SlotKind.Int32 => typeof(int),
        SlotKind.UInt32 => typeof(uint),
        SlotKind.Int64 => typeof(long),
        SlotKind.UInt64 => typeof(ulong),
        SlotKind.Single => typeof(float),
        SlotKind.Double => typeof(double),
        SlotKind.Pointer => typeof(nint),
        _ => throw new ArgumentException($"There is no CLR type for a {kind} slot."),
    };
}
