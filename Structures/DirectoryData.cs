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
			FullName = v;
		}

		/// <summary>
		/// The full name of the parent directory
		/// </summary>
		public string DirectoryName => FullName[..FullName.LastIndexOf('\\')];

		/// <summary>
		///  True if the directory data was populated
		/// </summary>
		public bool HasValue => !string.IsNullOrEmpty(FullName);

		/// <summary>
		///
		/// </summary>
		public DirectoryData Parent
		{
			get
			{
				if (FullName.TrimStart('\\').IndexOf('\\') > -1)
				{
					return new DirectoryData(DirectoryName);
				}

				return default;
			}
		}

		private string DebuggerDisplay => FullName;

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

			return FullName == mys.FullName;
		}

		/// <summary>
		///
		/// </summary>
		/// <returns></returns>
		public override int GetHashCode() => FullName.GetHashCode();
	}
}