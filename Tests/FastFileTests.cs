using Loxifi.FastIO.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace Loxifi.FastIO.Tests
{
	[TestClass]
	public class FastFileTests
	{

		[TestMethod]
		public void TestC()
		{
			int i = 0;

			foreach (FileData fd in FileSystemService.EnumerateFiles(new Structures.DirectoryData("C:\\"), true, false))
			{
				i++;
			}

			Debug.WriteLine(i);
		}

		[TestMethod]
		public void TestCHandler()
		{
			int i = 0;

			object ilock = new();

			FileSystemService.EnumerateFiles(new Structures.DirectoryData("C:\\"), (fd) =>
			{
				lock (ilock)
				{
					i++;
				}
			}, true, null);

			Debug.WriteLine(i);
		}

		[TestMethod]
		public void TestCMulti()
		{
			int i = 0;

			foreach (FileData fd in FileSystemService.EnumerateFiles(new Structures.DirectoryData("C:\\"), true, true))
			{
				i++;
			}

			Debug.WriteLine(i);
		}
	}
}