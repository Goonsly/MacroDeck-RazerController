using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeystoneDigital.RazerController.Hid;

internal interface IFeatureReportTransport : IDisposable
{
	bool SetFeature(byte[] buffer);

	bool GetFeature(byte[] buffer);
}

/// <summary>
/// Feature reports over a zero-access handle. Windows refuses any read or write open on a mouse
/// collection, administrator or not, but feature reports pass through a handle with no access rights.
/// </summary>
internal sealed partial class HidFeatureReportTransport : IFeatureReportTransport
{
	private const uint ShareReadWrite = 3;
	private const uint OpenExisting = 3;

	private readonly SafeFileHandle _handle;

	private HidFeatureReportTransport(SafeFileHandle handle) => _handle = handle;

	public static HidFeatureReportTransport? Open(string devicePath)
	{
		var handle = CreateFile(devicePath, 0, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

		if (handle.IsInvalid)
		{
			handle.Dispose();
			return null;
		}

		return new HidFeatureReportTransport(handle);
	}

	public bool SetFeature(byte[] buffer) => HidD_SetFeature(_handle, buffer, buffer.Length);

	public bool GetFeature(byte[] buffer) => HidD_GetFeature(_handle, buffer, buffer.Length);

	public void Dispose() => _handle.Dispose();

	[LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	private static partial SafeFileHandle CreateFile(
		string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

	[LibraryImport("hid.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int length);

	[LibraryImport("hid.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, int length);
}
