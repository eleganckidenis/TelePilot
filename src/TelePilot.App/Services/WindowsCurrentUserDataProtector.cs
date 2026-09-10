using System.Runtime.InteropServices;
using System.Text;

namespace TelePilot.App.Services;

public interface ICurrentUserDataProtector
{
    string Protect(string value);

    string Unprotect(string protectedValue);
}

public sealed class WindowsCurrentUserDataProtector : ICurrentUserDataProtector
{
    private const uint CryptProtectUiForbidden = 0x1;

    public string Protect(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureWindows();
        return Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), protect: true));
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        EnsureWindows();
        return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(protectedValue), protect: false));
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        DataBlob inputBlob = default;
        DataBlob outputBlob = default;
        try
        {
            inputBlob.Size = input.Length;
            inputBlob.Data = Marshal.AllocHGlobal(input.Length);
            Marshal.Copy(input, 0, inputBlob.Data, input.Length);

            var succeeded = protect
                ? CryptProtectData(
                    ref inputBlob,
                    null,
                    0,
                    0,
                    0,
                    CryptProtectUiForbidden,
                    ref outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    0,
                    0,
                    0,
                    0,
                    CryptProtectUiForbidden,
                    ref outputBlob);
            if (!succeeded)
            {
                throw new InvalidOperationException(
                    "System Windows nie zdołał zabezpieczyć danych nagrania.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            var output = new byte[outputBlob.Size];
            Marshal.Copy(outputBlob.Data, output, 0, outputBlob.Size);
            return output;
        }
        finally
        {
            if (inputBlob.Data != 0)
            {
                Marshal.FreeHGlobal(inputBlob.Data);
            }

            if (outputBlob.Data != 0)
            {
                LocalFree(outputBlob.Data);
            }
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Chronione zadania nagrywania są obecnie dostępne tylko w Windows.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public nint Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string? description,
        nint entropy,
        nint reserved,
        nint prompt,
        uint flags,
        ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        nint description,
        nint entropy,
        nint reserved,
        nint prompt,
        uint flags,
        ref DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
