using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ReadestStats.Core;

/// <summary>Protects local secrets for the current Windows user without a plaintext fallback.</summary>
public static class LocalSecretProtection
{
    private const uint CryptProtectUiForbidden = 0x1;
    private const int MaxPlaintextBytes = 16 * 1024;
    private const int MaxProtectedBytes = 64 * 1024;
    private const int MaxEncodedLength = ((MaxProtectedBytes + 2) / 3) * 4;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("ReadestStats.LocalSecretProtection.v1");

    public static string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Local secret protection requires Windows.");
        if (plaintext.Length > MaxPlaintextBytes || StrictUtf8.GetByteCount(plaintext) > MaxPlaintextBytes)
            throw new ArgumentException("The local secret exceeds the supported size.", nameof(plaintext));

        var bytes = StrictUtf8.GetBytes(plaintext);
        var inputHandle = default(GCHandle);
        var purposeHandle = default(GCHandle);
        var output = default(DataBlob);
        try
        {
            inputHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            purposeHandle = GCHandle.Alloc(Purpose, GCHandleType.Pinned);
            var input = new DataBlob(bytes.Length, inputHandle.AddrOfPinnedObject());
            var entropy = new DataBlob(Purpose.Length, purposeHandle.AddrOfPinnedObject());
            // No LOCAL_MACHINE flag: only the current user's Windows credentials can unprotect it.
            if (!CryptProtectData(ref input, "Readest Stats local secret", ref entropy,
                    IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output))
                throw new CryptographicException($"Windows could not protect the local secret (error {Marshal.GetLastWin32Error()}).");
            if (output.Length <= 0 || output.Length > MaxProtectedBytes || output.Data == IntPtr.Zero)
                throw new CryptographicException("Windows returned an invalid protected secret.");

            var protectedBytes = new byte[output.Length];
            try
            {
                Marshal.Copy(output.Data, protectedBytes, 0, protectedBytes.Length);
                return Convert.ToBase64String(protectedBytes);
            }
            finally { CryptographicOperations.ZeroMemory(protectedBytes); }
        }
        finally
        {
            FreeNativeBlob(ref output, clear: false);
            CryptographicOperations.ZeroMemory(bytes);
            if (inputHandle.IsAllocated) inputHandle.Free();
            if (purposeHandle.IsAllocated) purposeHandle.Free();
        }
    }

    public static bool TryUnprotect(string? protectedValue, out string? plaintext)
    {
        plaintext = null;
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(protectedValue)
            || protectedValue.Length > MaxEncodedLength)
            return false;

        var bytes = new byte[MaxProtectedBytes];
        var inputHandle = default(GCHandle);
        var purposeHandle = default(GCHandle);
        var output = default(DataBlob);
        var description = IntPtr.Zero;
        try
        {
            if (!Convert.TryFromBase64String(protectedValue, bytes, out var bytesWritten) || bytesWritten == 0)
                return false;
            inputHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            purposeHandle = GCHandle.Alloc(Purpose, GCHandleType.Pinned);
            var input = new DataBlob(bytesWritten, inputHandle.AddrOfPinnedObject());
            var entropy = new DataBlob(Purpose.Length, purposeHandle.AddrOfPinnedObject());
            if (!CryptUnprotectData(ref input, out description, ref entropy,
                    IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                || output.Length < 0 || output.Length > MaxPlaintextBytes
                || (output.Length > 0 && output.Data == IntPtr.Zero))
                return false;

            var plaintextBytes = new byte[output.Length];
            try
            {
                if (plaintextBytes.Length > 0)
                    Marshal.Copy(output.Data, plaintextBytes, 0, plaintextBytes.Length);
                plaintext = StrictUtf8.GetString(plaintextBytes);
                return true;
            }
            finally { CryptographicOperations.ZeroMemory(plaintextBytes); }
        }
        catch (Exception ex) when (ex is DecoderFallbackException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            FreeNativeBlob(ref output, clear: true);
            if (description != IntPtr.Zero) LocalFree(description);
            CryptographicOperations.ZeroMemory(bytes);
            if (inputHandle.IsAllocated) inputHandle.Free();
            if (purposeHandle.IsAllocated) purposeHandle.Free();
        }
    }

    private static void FreeNativeBlob(ref DataBlob blob, bool clear)
    {
        if (blob.Data == IntPtr.Zero) return;
        // Native output is bounded by the input blob; clear decrypted bytes before releasing it.
        try
        {
            if (clear && blob.Length > 0 && blob.Length <= MaxProtectedBytes)
                for (var offset = 0; offset < blob.Length; offset++)
                    Marshal.WriteByte(blob.Data, offset, 0);
        }
        finally
        {
            LocalFree(blob.Data);
            blob = default;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob(int length, IntPtr data)
    {
        public int Length = length;
        public IntPtr Data = data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string description,
        ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, out IntPtr description,
        ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
