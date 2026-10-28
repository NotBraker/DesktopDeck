using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DesktopDeck.Services;

/// <summary>
/// Secrets (sign-in tokens, client secrets, passwords, private addresses) encrypted with Windows DPAPI for the current user, kept in their own file
/// (never in settings.json). Only this Windows account on this PC can decrypt them. Other features read them through <see cref="Get"/>.
/// </summary>
public sealed class SecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DesktopDeck.secrets.v1");
    private readonly string _path;
    private readonly object _gate = new();

    public SecretStore(string directory) => _path = Path.Combine(Path.GetFullPath(directory), "secrets.json");
    public string FilePath => _path;

    public bool Has(string name) { lock (_gate) return Read().ContainsKey(name); }

    /// <summary>The decrypted key, or null if none is stored or it cannot be decrypted (for example after copying the file to another account).</summary>
    public string? Get(string name)
    {
        lock (_gate)
        {
            if (!Read().TryGetValue(name, out var stored)) return null;
            try { return Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(stored))); }
            catch (Exception ex) when (ex is FormatException or CryptographicFailure) { return null; }
        }
    }

    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        lock (_gate)
        {
            var all = Read();
            all[name] = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(value.Trim())));
            Write(all);
        }
    }

    public void Remove(string name)
    {
        lock (_gate)
        {
            var all = Read();
            if (all.Remove(name)) Write(all);
        }
    }

    /// <summary>Last four characters, for showing which key is saved without revealing it.</summary>
    public static string Hint(string? key) => string.IsNullOrEmpty(key) ? "" : key.Length <= 8 ? "••••" : "••••" + key[^4..];

    private Dictionary<string, string> Read()
    {
        try
        {
            if (!File.Exists(_path)) return new(StringComparer.Ordinal);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new(StringComparer.Ordinal);
        }
        catch (JsonException) { return new(StringComparer.Ordinal); }
    }

    private void Write(Dictionary<string, string> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static byte[] Protect(byte[] data) => Transform(data, protect: true);
    private static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = Blob.From(data);
        var entropy = Blob.From(Entropy);
        var output = new Blob();
        try
        {
            // CRYPTPROTECT_UI_FORBIDDEN: never prompt.
            bool ok = protect
                ? CryptProtectData(ref input, null, ref entropy, 0, 0, 0x1, ref output)
                : CryptUnprotectData(ref input, 0, ref entropy, 0, 0, 0x1, ref output);
            if (!ok) throw new CryptographicFailure(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            input.Free();
            entropy.Free();
            if (output.Data != 0) LocalFree(output.Data);
        }
    }

    public sealed class CryptographicFailure(int error) : Exception($"Windows could not encrypt or decrypt the key (error {error}).");

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public nint Data;
        public static Blob From(byte[] bytes)
        {
            var blob = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
            Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
            return blob;
        }
        public void Free()
        {
            if (Data == 0) return;
            // Clear plaintext before releasing the buffer.
            for (int i = 0; i < Size; i++) Marshal.WriteByte(Data, i, 0);
            Marshal.FreeHGlobal(Data);
            Data = 0;
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob data, string? description, ref Blob entropy, nint reserved, nint prompt, int flags, ref Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob data, nint description, ref Blob entropy, nint reserved, nint prompt, int flags, ref Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
