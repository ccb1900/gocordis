using Wasmtime;

namespace Gocordis.Runtime.Wasm;

/// <summary>
/// Reads a named WASM custom section (Go ABI: "module_type"). Wasmtime .NET
/// exposes no custom-section API, so we read the binary module produced by the
/// mature Wasmtime text converter using the WebAssembly binary format.
/// </summary>
public static class WasmCustomSections
{
    public static string? ReadFromWat(string wat, string sectionName) =>
        Read(Module.ConvertText(wat), sectionName);

    public static string? Read(byte[] wasm, string sectionName)
    {
        ArgumentNullException.ThrowIfNull(wasm);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var offset = 8; // magic + version
        while (offset < wasm.Length)
        {
            var id = wasm[offset++];
            var length = ReadLeb(wasm, ref offset);
            var end = offset + length;
            if (end > wasm.Length)
            {
                throw new InvalidDataException("malformed WASM section length");
            }

            if (id == 0)
            {
                var nameLength = ReadLeb(wasm, ref offset);
                var name = System.Text.Encoding.UTF8.GetString(wasm, offset, nameLength);
                offset += nameLength;
                if (name == sectionName)
                {
                    return System.Text.Encoding.UTF8.GetString(wasm, offset, end - offset);
                }
            }

            offset = end;
        }

        return null;
    }

    private static int ReadLeb(byte[] bytes, ref int offset)
    {
        int result = 0;
        var shift = 0;
        while (offset < bytes.Length && shift < 35)
        {
            var current = bytes[offset++];
            result |= (current & 0x7f) << shift;
            if ((current & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }

        throw new InvalidDataException("malformed LEB128 value");
    }
}
