using Loxifi.FastIO.Native;
using Loxifi.FastIO.Structures;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

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
		public static IEnumerable<FileData> EnumerateFiles(string path, bool recursive = false, int threads = 10, Action<DirectoryData>? onDirectory = null)
		{
			IEnumerable<FileData> files;

			if (recursive)
			{
				files = RecursiveEnumerateFiles(new DirectoryData(path), threads, onDirectory);
			}
			else
			{
				files = NonRecursiveEnumerateFiles(new DirectoryData(path), onDirectory);
			}

			return files;
		}

		/// <summary>
		/// Executes a multithreaded enumeration and invokes the provided actions when a file or directory is discovered
		/// </summary>
		/// <param name="directoryData"></param>
		/// <param name="onDirectory"></param>
		public static IEnumerable<FileData> RecursiveEnumerateFiles(DirectoryData directoryData, int threads = 10, Action<DirectoryData>? onDirectory = null)
		{
			int activeThreads = 0;

			Queue<DirectoryData> directories = new();

			BlockingCollection<FileData> files = new();

			directories.Enqueue(directoryData);

			SemaphoreSlim semaphoreSlim = new(1);

			object syncLock = new();

			bool completed = false;

			List<Thread> backgroundThreads = new();

			for(int  i = 0; i < threads; i++)
			{
				Thread thisThread = new(() =>
				{
					do
					{
						DirectoryData parent;

						semaphoreSlim.Wait();

						if (completed)
						{
							return;
						}

						lock (syncLock)
						{
							parent = directories.Dequeue();
							activeThreads++;
						}

						IntPtr hFile = IntPtr.Zero;

						try
						{
							hFile = NativeIO.FindFirstFileW(parent.FullName + "\\" + "*", out WIN32_FIND_DATA fd);

							// If we encounter an error, or there are no files/directories, we return no entries.
							if (hFile.ToInt64() != -1)
							{

								do
								{
									if (fd.cFileName is not ("." or ".."))
									{
										// If a directory (and not a Reparse Point), and the name is not "." or ".." which exist as concepts in the file system,
										// count the directory and add it to a list so we can iterate over it in parallel later on to maximize performance.
										if (IsRealDirectory(fd))
										{
											DirectoryData child = new(Path.Combine(parent.FullName, fd.cFileName));
											onDirectory?.Invoke(child);

											lock (syncLock)
											{
												directories.Enqueue(child);
												semaphoreSlim.Release();
											}
										}
										// Otherwise, if this is a file ("archive"), increment the file count.
										else if (IsFile(fd))
										{
											files.Add(new(
													fd.dwFileAttributes,
													Path.Combine(parent.FullName, fd.cFileName),
													DateTime.FromFileTimeUtc((fd.ftLastWriteTime.dwHighDateTime << 32) | (fd.ftLastWriteTime.dwLowDateTime & 0xFFFFFFFF)),
													(fd.nFileSizeHigh << 32) | fd.nFileSizeLow
											));
										}
									}
								}
								while (NativeIO.FindNextFileW(hFile, out fd));
							}
						}
						finally
						{
							if (hFile.ToInt64() != 0)
							{
								_ = NativeIO.FindClose(hFile);
							}

							lock (syncLock)
							{
								activeThreads--;

								if (activeThreads == 0 && directories.Count == 0)
								{
									completed = true;
									semaphoreSlim.Release(threads);
									files.CompleteAdding();
								}
							}
						}
					} while (true);

				});

				thisThread.Start();

				backgroundThreads.Add(thisThread);
			}

			foreach (FileData fd in files.GetConsumingEnumerable())
			{
				yield return fd;
			}
		}

		/// <summary>
		/// Executes a multithreaded enumeration and invokes the provided actions when a file or directory is discovered
		/// </summary>
		/// <param name="directoryData"></param>
		/// <param name="onDirectory"></param>
		public static IEnumerable<FileData> NonRecursiveEnumerateFiles(DirectoryData directoryData, Action<DirectoryData>? onDirectory = null)
		{
			IntPtr hFile = IntPtr.Zero;

			try
			{
				hFile = NativeIO.FindFirstFileW(directoryData.FullName + "\\" + "*", out WIN32_FIND_DATA fd);

				// If we encounter an error, or there are no files/directories, we return no entries.
				if (hFile.ToInt64() != -1)
				{

					do
					{
						if (fd.cFileName is not ("." or ".."))
						{
							// If a directory (and not a Reparse Point), and the name is not "." or ".." which exist as concepts in the file system,
							// count the directory and add it to a list so we can iterate over it in parallel later on to maximize performance.
							if (IsRealDirectory(fd))
							{
								DirectoryData child = new(Path.Combine(directoryData.FullName, fd.cFileName));
								onDirectory?.Invoke(child);
							}
							// Otherwise, if this is a file ("archive"), increment the file count.
							else if (IsFile(fd))
							{
								yield return new(
										fd.dwFileAttributes,
										Path.Combine(directoryData.FullName, fd.cFileName),
										DateTime.FromFileTimeUtc((fd.ftLastWriteTime.dwHighDateTime << 32) | (fd.ftLastWriteTime.dwLowDateTime & 0xFFFFFFFF)),
										(fd.nFileSizeHigh << 32) | fd.nFileSizeLow
								);
							}
						}
					}
					while (NativeIO.FindNextFileW(hFile, out fd));
				}
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