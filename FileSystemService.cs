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
        private const string REGULAR_SHARE_PATH_PREFIX = @"\\";

        private const string UNC_LOCAL_PATH_PREFIX = @"\\?\";

        private const string UNC_SHARE_PATH_PREFIX = @"\\?\UNC\";

        /// <summary>
        /// Enumerates directories in the given directory with the provided options
        /// </summary>
        /// <param name="directoryData"></param>
        /// <param name="recursive"></param>
        /// <param name="multithreaded"></param>
        /// <returns></returns>
        public static IEnumerable<DirectoryData> EnumerateDirectories(DirectoryData directoryData, bool recursive = false, bool multithreaded = false)
        {
            return !multithreaded
                ? EnumerateDirectoriesSingleThread(directoryData, recursive)
                : EnumerateDirectoriesMultiThread(directoryData, recursive);
        }

        /// <summary>
        /// Enumerates files in the given directory with the provided options
        /// </summary>
        /// <param name="directoryData"></param>
        /// <param name="recursive"></param>
        /// <param name="multithreaded"></param>
        /// <returns></returns>
        public static IEnumerable<FileData> EnumerateFiles(DirectoryData directoryData, bool recursive = false, bool multithreaded = false)
        {
            return !multithreaded
                ? EnumerateFilesSingleThread(directoryData, recursive)
                : EnumerateFilesMultiThread(directoryData, recursive);
        }

        /// <summary>
        /// Executes enumeration and invokes the provided actions when a file or directory is discovered
        /// </summary>
        /// <param name="directoryData"></param>
        /// <param name="onFile"></param>
        /// <param name="recursive"></param>
        /// <param name="onDirectory"></param>
        public static void EnumerateFiles(DirectoryData directoryData, Action<FileData> onFile, bool recursive = false, Action<DirectoryData>? onDirectory = null)
        {
            List<DirectoryData> directoryDatas = new();

            foreach (FileData fileData in Enumerate(directoryData, FoundDir))
            {
                onFile(fileData);
            }

            if (recursive)
            {
                Parallel.ForEach(directoryDatas, dd => EnumerateFiles(dd, onFile, recursive, onDirectory));
            }

            void FoundDir(DirectoryData dd)
            {
                if (recursive)
                {
                    directoryDatas.Add(dd);
                }

                onDirectory?.Invoke(dd);
            }
        }

        /// <summary>
        /// Executes enumeration and invokes the provided actions when a file or directory is discovered
        /// </summary>
        /// <param name="directoryData"></param>
        /// <param name="onFile"></param>
        /// <param name="recursive"></param>
        /// <param name="onDirectory"></param>
        public static async Task EnumerateFilesAsync(DirectoryData directoryData, Func<FileData, Task> onFile, bool recursive = false, Func<DirectoryData, Task>? onDirectory = null)
        {
            List<DirectoryData> directoryDatas = new();

            await foreach (FileData fileData in EnumerateAsync(directoryData, FoundDir))
            {
                await onFile(fileData);
            }

            if (recursive)
            {
                Parallel.ForEach(directoryDatas, async dd => await EnumerateFilesAsync(dd, onFile, recursive, onDirectory));
            }

            async Task FoundDir(DirectoryData dd)
            {
                if (recursive)
                {
                    directoryDatas.Add(dd);
                }

                if (onDirectory != null)
                {
                    await onDirectory(dd);
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
                NativeExceptionMapping(fileData.FullName, win32Error);
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
                NativeExceptionMapping(path, win32Error);
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

        /// <summary>
        /// If the path is >= 260 characters, this method escapes it. If not, the result is returned as-is
        /// </summary>
        public static string GetLongSafePath(string path)
        {
            if (path.Length < 260)
            {
                return path;
            }

            if (path.StartsWith("\\\\"))
            {
                return $"\\\\?\\UNC\\{path[2..]}";
            }

            return $"\\\\?\\{path}";
        }

        private static IEnumerable<FileData> Enumerate(DirectoryData directory, Action<DirectoryData>? onDirectory = null)
        {
            string path = directory.FullName;

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException("path", "The provided path is NULL or empty.");
            }

            if (path[^1] != '\\')
            {
                path += "\\";
            }

            IntPtr hFile = IntPtr.Zero;

            try
            {
                hFile = NativeIO.FindFirstFileW(path + "*", out WIN32_FIND_DATA fd);

                if (hFile.ToInt64() != -1)
                {
                    do
                    {
                        if (fd.cFileName is not ("." or ".."))
                        {
                            if (IsRealDirectory(fd))
                            {
                                onDirectory?.Invoke(new DirectoryData(Path.Combine(path, fd.cFileName)));
                            }
                            else if (IsFile(fd))
                            {
                                yield return new FileData(
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
            }
            finally
            {
                if (hFile.ToInt64() != 0)
                {
                    NativeIO.FindClose(hFile);
                }
            }
        }

        private static async IAsyncEnumerable<FileData> EnumerateAsync(DirectoryData directory, Func<DirectoryData, Task>? onDirectory = null)
        {
            string path = directory.FullName;

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException("path", "The provided path is NULL or empty.");
            }

            if (path[^1] != '\\')
            {
                path += "\\";
            }

            IntPtr hFile = IntPtr.Zero;

            try
            {
                hFile = NativeIO.FindFirstFileW(path + "*", out WIN32_FIND_DATA fd);

                if (hFile.ToInt64() != -1)
                {
                    do
                    {
                        if (fd.cFileName is not ("." or ".."))
                        {
                            if (IsRealDirectory(fd))
                            {
                                if (onDirectory != null)
                                {
                                    await onDirectory(new DirectoryData(Path.Combine(path, fd.cFileName)));
                                }
                            }
                            else if (IsFile(fd))
                            {
                                yield return new FileData(
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
            }
            finally
            {
                if (hFile.ToInt64() != 0)
                {
                    NativeIO.FindClose(hFile);
                }
            }
        }

        private static IEnumerable<DirectoryData> EnumerateDir(DirectoryData directory)
        {
            string path = directory.FullName;

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException("path", "The provided path is NULL or empty.");
            }

            if (path[^1] != '\\')
            {
                path += "\\";
            }

            IntPtr hFile = IntPtr.Zero;

            try
            {
                hFile = NativeIO.FindFirstFileW(path + "*", out WIN32_FIND_DATA fd);

                if (hFile.ToInt64() != -1)
                {
                    do
                    {
                        if (fd.cFileName is not ("." or "..") && IsRealDirectory(fd))
                        {
                            yield return new DirectoryData(Path.Combine(path, fd.cFileName));
                        }
                    }
                    while (NativeIO.FindNextFileW(hFile, out fd));
                }
            }
            finally
            {
                if (hFile.ToInt64() != 0)
                {
                    NativeIO.FindClose(hFile);
                }
            }
        }

        private static IEnumerable<DirectoryData> EnumerateDirectoriesMultiThread(DirectoryData directory, bool recursive)
        {
            ConcurrentBag<DirectoryData> toReturn = new();

            foreach (DirectoryData directoryData in EnumerateDir(directory))
            {
                toReturn.Add(directoryData);
            }

            if (!recursive)
            {
                return toReturn;
            }

            Parallel.ForEach(toReturn, directory2 =>
            {
                foreach (DirectoryData directoryData in EnumerateDirectoriesMultiThread(directory2, recursive))
                {
                    toReturn.Add(directoryData);
                }
            });

            return toReturn;
        }

        private static IEnumerable<DirectoryData> EnumerateDirectoriesSingleThread(DirectoryData directory, bool recursive = false)
        {
            List<DirectoryData> directoryDatas = new();

            foreach (DirectoryData directoryData in EnumerateDir(directory))
            {
                yield return directoryData;

                if (recursive)
                {
                    directoryDatas.Add(directoryData);
                }
            }

            foreach (DirectoryData dd in directoryDatas)
            {
                foreach (DirectoryData directoryData in EnumerateDirectoriesSingleThread(dd, recursive))
                {
                    yield return directoryData;
                }
            }
        }

        private static IEnumerable<FileData> EnumerateFilesMultiThread(DirectoryData directory, bool recursive)
        {
            ConcurrentBag<FileData> toReturn = new();
            List<DirectoryData> directories = new();

            foreach (FileData fileData in Enumerate(directory, directories.Add))
            {
                toReturn.Add(fileData);
            }

            if (!recursive)
            {
                return toReturn;
            }

            Parallel.ForEach(directories, directory2 =>
            {
                foreach (FileData fileData in EnumerateFilesMultiThread(directory2, recursive))
                {
                    toReturn.Add(fileData);
                }
            });

            return toReturn;
        }

        private static IEnumerable<FileData> EnumerateFilesSingleThread(DirectoryData directory, bool recursive = false)
        {
            List<DirectoryData> directoryDatas = new();
            Action<DirectoryData>? addAction = recursive ? directoryDatas.Add : null;

            foreach (FileData fileData in Enumerate(directory, addAction))
            {
                yield return fileData;
            }

            foreach (DirectoryData dd in directoryDatas)
            {
                foreach (FileData fileData in EnumerateFilesSingleThread(dd, recursive))
                {
                    yield return fileData;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Ensure(ref DirectoryData data)
        {
            if (string.IsNullOrWhiteSpace(data.FullName))
            {
                throw new ArgumentNullException("path", "The provided path is NULL or empty.");
            }
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

        private static string ToLocalRegularPath(string uncLocalPath) => uncLocalPath[UNC_LOCAL_PATH_PREFIX.Length..];

        private static string ToRegularPath(string anyFullname)
        {
            if (anyFullname.StartsWith(UNC_SHARE_PATH_PREFIX, StringComparison.Ordinal))
            {
                return ToShareRegularPath(anyFullname);
            }

            if (anyFullname.StartsWith(UNC_LOCAL_PATH_PREFIX, StringComparison.Ordinal))
            {
                return ToLocalRegularPath(anyFullname);
            }

            return anyFullname;
        }
    }
}