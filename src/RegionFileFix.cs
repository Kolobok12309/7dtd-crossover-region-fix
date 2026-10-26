using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HarmonyLib;

public class RegionFileFixMod : IModApi
{
	public void InitMod(Mod _modInstance)
	{
		new Harmony("sova.regionfilefix").PatchAll(typeof(RegionFileFixMod).Assembly);
		Log.Out("[RegionFileFix] patched RegionFileSectorBased.Get, SdFile.Exists, SdFile.Delete");
	}
}

[HarmonyPatch(typeof(RegionFileSectorBased), nameof(RegionFileSectorBased.Get))]
public static class RegionFileSectorBasedGetPatch
{
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern uint GetFileAttributesW(string lpFileName);

	static readonly HashSet<string> reported = new HashSet<string>();

	static bool Prefix(string dir, int rX, int rZ, string ext, ref RegionFile __result)
	{
		string path = RegionFile.ConstructFullFilePath(dir, rX, rZ, ext);
		if (!SdFile.Exists(path))
		{
			return true;
		}
		try
		{
			using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
			return true;
		}
		catch (FileNotFoundException) { }

		lock (reported)
		{
			if (reported.Add(path))
			{
				uint attr = GetFileAttributesW(path);
				int err = Marshal.GetLastWin32Error();
				string files;
				try { files = string.Join(", ", Directory.GetFiles(dir)); } catch (Exception e) { files = "ERR " + e.Message; }
				Log.Warning("[RegionFileFix] ghost file: Exists=true but Open=FileNotFound: " + path
					+ " | File.Exists=" + File.Exists(path)
					+ " FileInfo.Exists=" + new FileInfo(path).Exists
					+ " GetFileAttributesW=0x" + attr.ToString("X") + " lastError=" + err
					+ " | dir files: [" + files + "]");
			}
		}

		using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
		__result = new RegionFileV2(path, rX, rZ, null, 1);
		return false;
	}
}

[HarmonyPatch(typeof(SdFile), nameof(SdFile.Exists))]
public static class SdFileExistsPatch
{
	const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;
	const int MaxReports = 50;
	static int reports;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern uint GetFileAttributesW(string lpFileName);

	static void Postfix(string path, ref bool __result)
	{
		if (!__result || string.IsNullOrEmpty(path))
		{
			return;
		}
		uint attr = GetFileAttributesW(path);
		int err = Marshal.GetLastWin32Error();
		if (attr != INVALID_FILE_ATTRIBUTES || (err != 0 && err != 2 && err != 3))
		{
			return;
		}
		try
		{
			using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { }
			return;
		}
		catch (FileNotFoundException) { }
		catch (DirectoryNotFoundException) { }
		catch (Exception)
		{
			return;
		}
		__result = false;
		if (System.Threading.Interlocked.Increment(ref reports) <= MaxReports)
		{
			Log.Warning("[RegionFileFix] SdFile.Exists ghost -> false: " + path + " (lastError=" + err + ")");
		}
	}
}

// File.Delete on a missing file must be a no-op, but under CrossOver it can throw
// IOException "Unknown error (0x...)" with a garbage error code. Swallow it only when
// the file is really absent: GetFileAttributesW fails AND the parent directory listing
// does not contain the file (error codes are not trusted, they are garbage here).
[HarmonyPatch(typeof(SdFile), nameof(SdFile.Delete))]
public static class SdFileDeletePatch
{
	const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;
	const int MaxReports = 50;
	static int reports;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern uint GetFileAttributesW(string lpFileName);

	static Exception Finalizer(Exception __exception, string path)
	{
		if (!(__exception is IOException) || string.IsNullOrEmpty(path))
		{
			return __exception;
		}
		string reason;
		bool missing = IsReallyMissing(path, out reason);
		if (System.Threading.Interlocked.Increment(ref reports) <= MaxReports)
		{
			Log.Warning("[RegionFileFix] SdFile.Delete failed: " + path + " (" + __exception.Message + "); "
				+ reason + (missing ? " -> file is missing, error ignored" : " -> error kept"));
		}
		return missing ? null : __exception;
	}

	static bool IsReallyMissing(string path, out string reason)
	{
		uint attr = GetFileAttributesW(path);
		int err = Marshal.GetLastWin32Error();
		reason = "GetFileAttributesW=0x" + attr.ToString("X") + " lastError=" + err;
		if (attr != INVALID_FILE_ATTRIBUTES)
		{
			return false;
		}
		try
		{
			string full = Path.GetFullPath(path);
			string dir = Path.GetDirectoryName(full);
			string name = Path.GetFileName(full);
			if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
			{
				return false;
			}
			foreach (string entry in Directory.GetFileSystemEntries(dir))
			{
				if (string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase))
				{
					reason += ", present in directory listing";
					return false;
				}
			}
			reason += ", not in directory listing";
			return true;
		}
		catch (Exception e)
		{
			reason += ", listing failed: " + e.Message;
			return false;
		}
	}
}
