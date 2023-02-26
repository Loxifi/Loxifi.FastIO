﻿using System.Diagnostics;

namespace Loxifi.FastIO.Structures
{
	/// <summary>
	/// Struct representing file information from disk
	/// </summary>
	[DebuggerDisplay("{DebuggerDisplay,nq}")]
	public readonly struct FileData
	{
		/// <summary>
		/// File Attributes
		/// </summary>
		public readonly FileAttributes Attributes;

		/// <summary>
		/// Full path to file
		/// </summary>
		public readonly string FullName;

		/// <summary>
		/// Last Modified
		/// </summary>
		public readonly DateTime LastWriteTime;

		/// <summary>
		/// File Length
		/// </summary>
		public readonly ulong Length;

		/// <summary>
		///
		/// </summary>
		/// <param name="attributes"></param>
		/// <param name="fullName"></param>
		/// <param name="lastWriteTime"></param>
		/// <param name="length"></param>
		/// <exception cref="ArgumentException"></exception>
		public FileData(FileAttributes attributes, string fullName, DateTime lastWriteTime, ulong length)
		{
			if (string.IsNullOrWhiteSpace(fullName))
			{
				throw new ArgumentException($"'{nameof(fullName)}' cannot be null or whitespace.", nameof(fullName));
			}

			FullName = fullName;
			Attributes = attributes;
			LastWriteTime = lastWriteTime;
			Length = length;
		}

		/// <summary>
		/// Gets a struct representing the parent directory
		/// </summary>
		public DirectoryData Directory => new(DirectoryName);

		/// <summary>
		/// String path of the folder containing this file
		/// </summary>
		public string DirectoryName => FullName[..FullName.LastIndexOf('\\')];

		/// <summary>
		/// FullName property in a format compatible with lengths > 260 characters
		/// </summary>
		public string FullNameLong
		{
			get
			{
				if (FullName.Length < 260 || FullName.StartsWith("\\\\?\\"))
				{
					return FullName;
				}

				return $"\\\\?\\{FullName}";
			}
		}

		/// <summary>
		/// True if this struct has been populated
		/// </summary>
		public bool HasValue => !string.IsNullOrWhiteSpace(FullName);

		/// <summary>
		/// Debugger only
		/// </summary>
		private string DebuggerDisplay => FullName;

		/// <summary>
		///
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		public static bool operator !=(FileData a, FileData b) => b.Equals(a);

		/// <summary>
		///
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		public static bool operator ==(FileData a, FileData b) => a.Equals(b);

		/// <summary>
		/// True if this file is a child (recursive) of the provided directory
		/// </summary>
		/// <param name="directoryData"></param>
		/// <returns></returns>
		public bool ChildOf(DirectoryData directoryData) => DirectoryName.StartsWith(directoryData.FullName + "\\") || directoryData.FullName == DirectoryName;

		/// <summary>
		///
		/// </summary>
		/// <param name="obj"></param>
		/// <returns></returns>
		public override bool Equals(object? obj)
		{
			if (obj is not FileData mys)
			{
				return false;
			}

			return FullName == mys.FullName;
		}

		/// <summary>
		///
		/// </summary>
		/// <returns></returns>
		public override int GetHashCode() => FullName.GetHashCode();
	}
}