using Loxifi.FastIO.Native;
using Loxifi.FastIO.Structures;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Loxifi.FastIO
{
	/// <summary>
	///
	/// </summary>
	public static class FileSystemService
	{
		//TODO: Async methods should be joined with sync

		private const string REGULAR_SHARE_PATH_PREFIX = @"\\";

		private const string UNC_LOCAL_PATH_PREFIX = @"\\?\";

		private const string UNC_SHARE_PATH_PREFIX = @"\\?\UNC\";

		/// <summary>
		/// Enumerates files in the given directory with the provided options
		/// </summary>
		/// <param name="directoryData"></param>
		/// <param name="recursive"></param>
		/// <param name="multithreaded"></param>
		/// <returns></returns>
		public static IEnumerable<FileData> EnumerateFiles(DirectoryData directoryData, bool recursive = false, bool multithreaded = false)
		{
			IEnumerable<FileData> files;

			if (multithreaded)
			{
				files = EnumerateFilesMultiThread(directoryData, recursive);
			}
			else
			{
				files = EnumerateFilesSingleThread(directoryData, recursive);
			}

			return files;
		}

		/// <summary>
		/// Executes a multithreaded enumeration and invokes the provided actions when a file or directory is discovered
		/// </summary>
		/// <param name="directoryData"></param>
		/// <param name="onFile"></param>
		/// <param name="recursive"></param>
		/// <param name="onDirectory"></param>
		public static void EnumerateFiles(DirectoryData directoryData, Action<FileData> onFile, bool recursive = false, Action<DirectoryData>? onDirectory = null)
		{
			List<DirectoryData> directoryDatas = new();

			void FoundDir(DirectoryData dd)
			{
				if (recursive)
				{
					directoryDatas.Add(dd);
				}

				onDirectory?.Invoke(dd);
			}

			foreach (FileData fileData in Enumerate(directoryData, FoundDir))
			{
				onFile.Invoke(fileData);
			}

			if (!recursive)
			{
				return;
			}

			_ = Parallel.ForEach(directoryDatas, dd => EnumerateFiles(dd, onFile, recursive, onDirectory));
		}

		/// <summary>
		/// Executes a multithreaded enumeration and invokes the provided actions when a file or directory is discovered
		/// </summary>
		/// <param name="directoryData"></param>
		/// <param name="onFile"></param>
		/// <param name="recursive"></param>
		/// <param name="onDirectory"></param>
		public static async Task EnumerateFilesAsync(DirectoryData directoryData, Func<FileData, Task> onFile, bool recursive = false, Func<DirectoryData, Task>? onDirectory = null)
		{
			List<DirectoryData> directoryDatas = new();

			async Task FoundDir(DirectoryData dd)
			{
				if (recursive)
				{
					directoryDatas.Add(dd);
				}

				if (onDirectory is not null)
				{
					await onDirectory.Invoke(dd);
				}
			}

			await foreach (FileData fileData in EnumerateAsync(directoryData, FoundDir))
			{
				await onFile.Invoke(fileData);
			}

			if (!recursive)
			{
				return;
			}

			_ = Parallel.ForEach(directoryDatas, async dd => await EnumerateFilesAsync(dd, onFile, recursive, onDirectory));
		}

		/// <summary>
		/// Opens a <see cref="FileStream"/> for access at the given path. Ensure stream is correctly disposed.
		/// </summary>
		public static FileStream Open(FileData fileData, FileAccess fileAccess, FileMode fileOption = FileMode.Open, FileShare shareMode = FileShare.Read, int buffer = 0)
		{
			SafeFileHandle fileHandle = NativeIO.CreateFileW(GetLongSafePath(fileData.FullName), fileAccess, shareMode, IntPtr.Zero, fileOption, 0, IntPtr.Zero);

			int win32Error = Marshal.GetLastWin32Error();
			if (fileHandle.IsInvalid)
			{
				NativeExceptionMapping(fileData.FullName, win32Error); // Throws an exception
			}

			return buffer > 0 ? new FileStream(fileHandle, fileAccess, buffer) : new FileStream(fileHandle, fileAccess);
		}

		/// <summary>
		/// Opens a <see cref="FileStream"/> for access at the given path. Ensure stream is correctly disposed.
		/// </summary>
		public static FileStream Open(string path, FileAccess fileAccess, FileMode fileOption = FileMode.Open, FileShare shareMode = FileShare.Read, int buffer = 0)
		{
			path = GetLongSafePath(path);

			SafeFileHandle fileHandle = NativeIO.CreateFileW(path, fileAccess, shareMode, IntPtr.Zero, fileOption, 0, IntPtr.Zero);

			int win32Error = Marshal.GetLastWin32Error();
			if (fileHandle.IsInvalid)
			{
				NativeExceptionMapping(path, win32Error); // Throws an exception
			}

			return buffer > 0 ? new FileStream(fileHandle, fileAccess, buffer) : new FileStream(fileHandle, fileAccess);
		}

		/// <summary>
		/// Converts an unc path to a share regular path
		/// </summary>
		/// <param name="uncSharePath">Unc Path</param>
		/// <example>\\?\UNC\server\share >> \\server\share</example>
		/// <returns>QuickIOShareInfo Regular Path</returns>
		public static string ToShareRegularPath(string uncSharePath) => REGULAR_SHARE_PATH_PREFIX + uncSharePath[UNC_SHARE_PATH_PREFIX.Length..];

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void Ensure(ref DirectoryData data)
		{
			if (string.IsNullOrWhiteSpace(data.FullName))
			{
				throw new ArgumentNullException("path", "The provided path is NULL or empty.");
			}
		}

		private static async IAsyncEnumerable<FileData> EnumerateAsync(DirectoryData directory, Func<DirectoryData, Task>? onDirectory = null)
		{
			string path = directory.FullName;

			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentNullException("path", "The provided path is NULL or empty.");
			}

			// If the provided path doesn't end in a backslash, append one.
			if (path.Last() != '\\')
			{
				path += '\\';
			}

			IntPtr hFile = IntPtr.Zero;

			try
			{
				hFile = NativeIO.FindFirstFileW(path + "*", out WIN32_FIND_DATA fd);

				// If we encounter an error, or there are no files/directories, we return no entries.
				if (hFile.ToInt64() == -1)
				{
					yield break;
				}

				do
				{
					if (fd.cFileName is not ("." or ".."))
					{
						// If a directory (and not a Reparse Point), and the name is not "." or ".." which exist as concepts in the file system,
						// count the directory and add it to a list so we can iterate over it in parallel later on to maximize performance.
						if (IsRealDirectory(fd))
						{
							if (onDirectory != null)
							{
								await onDirectory.Invoke(new DirectoryData(Path.Combine(path, fd.cFileName)));
							}
						}
						else if (IsFile(fd))
						{
							yield return new(
									fd.dwFileAttributes,
									Path.Combine(path, fd.cFileName),
									DateTime.FromFileTimeUtc((fd.ftLastWriteTime.dwHighDateTime << 32) | (fd.ftLastWriteTime.dwLowDateTime & 0xFFFFFFFF)),
									(fd.nFileSizeHigh << 32) | fd.nFileSizeLow
								);
						}
					}
				}
				while (NativeIO.FindNextFileW(hFile, out fd));
			}
			finally
			{
				if (hFile.ToInt64() != 0)
				{
					_ = NativeIO.FindClose(hFile);
				}
			}
		}

		private static IEnumerable<FileData> Enumerate(DirectoryData directory, Action<DirectoryData>? onDirectory = null)
		{
			string path = directory.FullName;

			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentNullException("path", "The provided path is NULL or empty.");
			}

			ConcurrentBag<FileData> toReturn = new();

			// If the provided path doesn't end in a backslash, append one.
			if (path.Last() != '\\')
			{
				path += '\\';
			}

			IntPtr hFile = IntPtr.Zero;

			try
			{
				hFile = NativeIO.FindFirstFileW(path + "*", out WIN32_FIND_DATA fd);

				// If we encounter an error, or there are no files/directories, we return no entries.
				if (hFile.ToInt64() == -1)
				{
					yield break;
				}

				do
				{
					if (fd.cFileName is not ("." or ".."))
					{
						// If a directory (and not a Reparse Point), and the name is not "." or ".." which exist as concepts in the file system,
						// count the directory and add it to a list so we can iterate over it in parallel later on to maximize performance.
						if (IsRealDirectory(fd))
						{
							onDirectory?.Invoke(new DirectoryData(Path.Combine(path, fd.cFileName)));
						}
						// Otherwise, if this is a file ("archive"), increment the file count.
						else if (IsFile(fd))
						{
							yield return new(
									fd.dwFileAttributes,
									Path.Combine(path, fd.cFileName),
									DateTime.FromFileTimeUtc((fd.ftLastWriteTime.dwHighDateTime << 32) | (fd.ftLastWriteTime.dwLowDateTime & 0xFFFFFFFF)),
									(fd.nFileSizeHigh << 32) | fd.nFileSizeLow
								);
						}
					}
				}
				while (NativeIO.FindNextFileW(hFile, out fd));
			}
			finally
			{
				if (hFile.ToInt64() != 0)
				{
					_ = NativeIO.FindClose(hFile);
				}
			}
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="directory"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentNullException"></exception>
		private static IEnumerable<FileData> EnumerateFilesMultiThread(DirectoryData directory, bool recursive)
		{
			ConcurrentBag<FileData> toReturn = new();
			List<DirectoryData> directories = new();

			foreach (FileData fd in Enumerate(directory, directories.Add))
			{
				toReturn.Add(fd);
			}

			//This is dumb since it never executes multithread but the code path
			//should be dynamic and match the other sigs
			if (!recursive)
			{
				return toReturn;
			}

			// Iterate over each discovered directory in parallel to maximize file/directory counting performance,
			// calling itself recursively to traverse each directory completely.
			_ = Parallel.ForEach(
				directories,
				directory =>
				{
					foreach (FileData fd in EnumerateFilesMultiThread(directory, recursive))
					{
						toReturn.Add(fd);
					}
				});

			return toReturn;
		}

		/// <summary>
		/// Fast enumerates the children of a provided directory
		/// </summary>
		/// <param name="directory"></param>
		/// <param name="recursive"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentNullException"></exception>
		private static IEnumerable<FileData> EnumerateFilesSingleThread(DirectoryData directory, bool recursive = false)
		{
			List<DirectoryData> directoryDatas = new();

			Action<DirectoryData>? addAction = recursive ? directoryDatas.Add : null;

			foreach (FileData fd in Enumerate(directory, addAction))
			{
				yield return fd;
			}

			foreach (DirectoryData dd in directoryDatas)
			{
				foreach (FileData fd in EnumerateFilesSingleThread(dd, recursive))
				{
					yield return fd;
				}
			}
		}

		/// <summary>
		/// If the path is >= 260 characters, this method escapes it. If not, the result is returned as-is
		/// </summary>
		/// <param name="path"></param>
		/// <returns></returns>
		public static string GetLongSafePath(string path)
		{
			if(path.Length < 260)
			{
				return path;
			}

			if (path.StartsWith("\\\\"))
			{
				return $"\\\\?\\UNC\\{path[2..]}";
			}

			return $"\\\\?\\{path}";
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool IsFile(WIN32_FIND_DATA fd) => (fd.dwFileAttributes & FileAttributes.Directory) == 0 && (fd.dwFileAttributes & FileAttributes.ReparsePoint) == 0;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool IsRealDirectory(WIN32_FIND_DATA fd) => (fd.dwFileAttributes & FileAttributes.Directory) != 0 && (fd.dwFileAttributes & FileAttributes.ReparsePoint) == 0;

		private static void NativeExceptionMapping(string path, int errorCode)
		{
			if (errorCode == 0)
			{
				return;
			}

			string affectedPath = ToRegularPath(path);

			Win32Exception innerException = new(errorCode);

			throw errorCode switch
			{
				2 => new FileNotFoundException("The system can not find the file specified", affectedPath),
				3 => new FileNotFoundException("The system can not find the file specified", affectedPath),
				5 => new UnauthorizedAccessException(affectedPath, innerException),
				32 => new IOException("The file is in use by another process '{affectedPath}'", innerException),
				_ => innerException,
			};
		}

		/// <summary>
		/// Converts an unc path to a local regular path
		/// </summary>
		/// <param name="uncLocalPath">Unc Path</param>
		/// <example>\\?\C:\temp\file.txt >> C:\temp\file.txt</example>
		/// <returns>Local Regular Path</returns>
		private static string ToLocalRegularPath(string uncLocalPath) => uncLocalPath[UNC_LOCAL_PATH_PREFIX.Length..];

		/// <summary>
		/// Converts unc path to regular path
		/// </summary>
		private static string ToRegularPath(string anyFullname)
		{
			// First: Check for UNC QuickIOShareInfo
			if (anyFullname.StartsWith(UNC_SHARE_PATH_PREFIX, StringComparison.Ordinal))
			{
				return ToShareRegularPath(anyFullname); // Convert
			}

			if (anyFullname.StartsWith(UNC_LOCAL_PATH_PREFIX, StringComparison.Ordinal))
			{
				return ToLocalRegularPath(anyFullname); // Convert
			}

			return anyFullname;
		}
	}
}