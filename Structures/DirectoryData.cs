using System.Diagnostics;

namespace Loxifi.FastIO.Structures
{
	/// <summary>
	///
	/// </summary>
	[DebuggerDisplay("{DebuggerDisplay,nq}")]
	public readonly struct DirectoryData
	{
		/// <summary>
		/// The full path and name of this directory
		/// </summary>
		public readonly string FullName;

		/// <summary>
		///
		/// </summary>
		/// <param name="v"></param>
		public DirectoryData(string v)
		{
			if (v[^1] == Path.DirectorySeparatorChar)
			{
				this.FullName = v[..^1];
			}

			this.FullName = v;
		}

		/// <summary>
		/// The full name of the parent directory
		/// </summary>
		public string DirectoryName => this.FullName[..this.FullName.LastIndexOf('\\')];

		/// <summary>
		///  True if the directory data was populated
		/// </summary>
		public bool HasValue => !string.IsNullOrEmpty(this.FullName);

		/// <summary>
		///
		/// </summary>
		public DirectoryData Parent
		{
			get
			{
				if (this.FullName.TrimStart('\\').IndexOf('\\') > -1)
				{
					return new DirectoryData(this.DirectoryName);
				}

				return default;
			}
		}

		private string DebuggerDisplay => this.FullName;

		/// <summary>
		///
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		public static bool operator !=(DirectoryData a, DirectoryData b) => b.Equals(a);

		/// <summary>
		///
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		public static bool operator ==(DirectoryData a, DirectoryData b) => a.Equals(b);

		/// <summary>
		///
		/// </summary>
		/// <param name="obj"></param>
		/// <returns></returns>
		public override bool Equals(object? obj)
		{
			if (obj is not DirectoryData mys)
			{
				return false;
			}

			return this.FullName == mys.FullName;
		}

		/// <summary>
		/// True if this file is a child (recursive) of the provided directory
		/// </summary>
		/// <param name="directoryData"></param>
		/// <returns></returns>
		public bool ChildOf(DirectoryData directoryData) => this.FullName.StartsWith(directoryData.FullName + Path.DirectorySeparatorChar);

		/// <summary>
		///
		/// </summary>
		/// <returns></returns>
		public override int GetHashCode() => this.FullName.GetHashCode();
	}
}