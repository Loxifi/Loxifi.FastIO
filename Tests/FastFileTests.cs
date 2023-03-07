using Loxifi.FastIO.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace Loxifi.FastIO.Tests
{
	[TestClass]
	public class FastFileTests
	{
		[TestMethod]
		public void TestOpen()
		{
			string uri = "\\\\192.168.0.218\\Mirror\\3TB\\Downloaded from LeakedBB.com -- 2015 oct-dec GoneWild archive web rip 10k images\\My Web Sites\\rhymenoserus\\[ LeakedBB.com_Repost_4 ]\\hts-cache\\new\\http_\\www.xxxxselfie.com\\photo\\who-wants-to-titi-fuck-me-and-leave-a-mark-like-this.html";

			using Stream stream = FileSystemService.Open(uri, FileAccess.Read);
		}

		//[TestMethod]
		//public void TestC()
		//{
		//	int i = 0;

		//	foreach (FileData fd in FileSystemService.EnumerateFiles(new Structures.DirectoryData("C:\\"), true, false))
		//	{
		//		i++;
		//	}

		//	Debug.WriteLine(i);
		//}

		//[TestMethod]
		//public void TestCHandler()
		//{
		//	int i = 0;

		//	object ilock = new();

		//	FileSystemService.EnumerateFiles(new Structures.DirectoryData("C:\\"), (fd) =>
		//	{
		//		lock (ilock)
		//		{
		//			i++;
		//		}
		//	}, true, null);

		//	Debug.WriteLine(i);
		//}

		//[TestMethod]
		//public void TestCMulti()
		//{
		//	int i = 0;

		//	foreach (FileData fd in FileSystemService.EnumerateFiles(new Structures.DirectoryData("C:\\"), true, true))
		//	{
		//		i++;
		//	}

		//	Debug.WriteLine(i);
		//}
	}
}