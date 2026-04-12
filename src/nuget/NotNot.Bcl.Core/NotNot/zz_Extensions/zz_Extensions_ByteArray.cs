// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Blake3;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Helpers;
// using Newtonsoft.Json.Linq; // Removed - no longer needed
using Nito.AsyncEx.Synchronous;
using NotNot;
using NotNot._internal.Threading;
using NotNot.Collections.Advanced;
using NotNot.Collections.SpanLike;
using NotNot.Data;
using NotNot.Diagnostics;

//using Xunit.Sdk;

//using CommunityToolkit.HighPerformance;
//using DotNext;

[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]

public static class zz_Extensions_ByteArray
{

	/// <summary>
	/// convert into a base64 encoded string
	/// </summary>
	/// <param name="input"></param>
	/// <param name="stripPadding">default false.  if true, will remove the customary '=' padding (if any)</param>
	/// <returns></returns>
	public static string _ToBase64(this byte[] input, bool stripPadding = false)
	{
		var base64 = Convert.ToBase64String(input);

		if (stripPadding is true && base64.EndsWith('='))
		{
			return base64.TrimEnd('=');
		}
		return base64;
	}


	//[Conditional("TEST")]
	//public static void _UnitTests()
	//{
	//	//unit tests

	//	//string to bytes roundtrip
	//	string helloWorld = "hello, World!";
	//	var toBytes = helloWorld._ToBytes(Encoding.UTF8, true);
	//	string backToString;
	//	if (!toBytes._TryConvertToStringWithPreamble(out backToString))
	//	{
	//		__.GetLogger()._EzError(false);
	//	}
	//	__.GetLogger()._EzError(helloWorld == backToString);

	//	//compression roundtrip
	//	var originalBytes = helloWorld._ToBytes(Encoding.Unicode);
	//	var compressedBytes = new byte[0];
	//	int compressLength;
	//	originalBytes._Compress(ref compressedBytes, out compressLength);
	//	var decompressedBytes = new byte[0];
	//	int decompressLength;
	//	var result = compressedBytes._TryDecompress(ref decompressedBytes, out decompressLength);
	//	__.GetLogger()._EzError(result);
	//	var decompressedString = decompressedBytes._ToUnicodeString(Encoding.Unicode, 0, decompressLength);
	//	__.GetLogger()._EzError(helloWorld == decompressedString);

	//	//decompression safe errors (no exceptions)

	//	var emptyBytes = new byte[0];
	//	result = emptyBytes._TryDecompress(ref decompressedBytes, out decompressLength);
	//	__.GetLogger()._EzError(result, "0 bytes should decode to 0 len output");

	//	var bigEmptyBytes = new byte[1000];
	//	result = bigEmptyBytes._TryDecompress(ref decompressedBytes, out decompressLength);
	//	__.GetLogger()._EzError(!result);

	//	var bigJunkBytes = new byte[1000];
	//	bigJunkBytes.Fill(byte.MaxValue);
	//	result = bigJunkBytes._TryDecompress(ref decompressedBytes, out decompressLength);
	//	__.GetLogger()._EzError(!result);

	//}


	private static Encoding[] possibleEncodings = { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode };

	/// <summary>
	///  <para>Sometimes you need to convert a string directly into bytes.  This converts it back.  (NOT the same as base64 encoding)</para>
	///  convert a byte[] to a string (will detect byte encoding automatically)
	///    <para>Note: if no encoding preamble is detected, FALSE is returned.</para>
	/// </summary>
	/// <param name="input"></param>
	/// <returns></returns>
	/// <remarks>
	///    Note that ASCII does not include a preamble, so will always fail.  use .<see cref="ToUnicodeString" />
	///    (Encoding.ASCII) explicitly if you have ascii text
	/// </remarks>   
	public static bool _TryConvertToStringWithPreamble(this byte[] input, out string output, int start = 0,
		int? count = null)
	{
		count = count ?? input.Length - start;

		Encoding encoding = null;
		byte[] preamble = null;
		foreach (var possibleEncoding in possibleEncodings)
		{
			//var potentialEncoding = encodingInfo.GetEncoding();
			preamble = possibleEncoding.GetPreamble();

			if (preamble.Length > 0) //only allow encodings that use preambles
			{
				if (input._Compare(preamble, start, 0, preamble.Length) == 0)
				{
					encoding = possibleEncoding;
					break;
				}
			}
		}

		if (encoding == null)
		{
			////if no encoding detected, default fail
			output = null;
			return false;
		}

		output = encoding.GetString(input, start + preamble.Length, count.Value - preamble.Length);

		return true;
	}


	/// <summary>
	/// <para>Sometimes you need to convert a string directly into bytes.  This converts it back.  (NOT the same as base64 encoding)</para>
	///    convert a byte[] to a string.  no preamble is allowed, it just quickly converts the bytes to the given
	///    <see cref="encoding" /> (no safety checks!)
	///    <para>Note: if no encoding is specified, UTF8 is used</para>
	/// </summary>
	public static string _ToUnicodeString(this byte[] input)
	{
		return input._ToUnicodeString(Encoding.UTF8, 0, input.Length);
	}

	/// <summary>
	/// <para>Sometimes you need to convert a string directly into bytes.  This converts it back.  (NOT the same as base64 encoding)</para>
	///    convert a byte[] to a string.  no preamble is allowed, it just quickly converts the bytes to the given
	///    <see cref="encoding" /> (no safety checks!)
	///    <para>Note: if no encoding is specified, UTF8 is used</para>
	/// </summary>
	public static string _ToUnicodeString(this byte[] input, Encoding encoding, int index = 0, int? count = null)
	{
		count = count ?? input.Length - index;
		foreach (var possible in possibleEncodings)
		{
			__.GetLogger()._EzError(input._FindArrayInArray(possible.GetPreamble()) != 0,
				$"input starts with {possible}.Preamble, should use Preamble aware method instead");
		}

		return encoding.GetString(input, index, count.Value);
	}

	public static string _ToHex(this byte[] stringBytes)
	{
		StringBuilder outputString = new(stringBytes.Length * 2);
		foreach (var value in stringBytes)
		{
			outputString.AppendFormat(CultureInfo.InvariantCulture, "{0:x2}", value);
		}

		return outputString.ToString();
	}

	//public static string ToString(this byte[] stringBytes)
	//{

	//    var encoding = Encoding.GetEncoding()
	//    return Encoding.GetEncoding.GetString(unicodeStringBytes);


	//}

	public static void _ToArray(this byte[] bytes, int start, int count, out float[] floats)
	{
		//__.ERROR.AssertOnce("add unitTest for endianness");

		__.GetLogger()._EzError(count % 4 == 0, "count should be multiple of 4!!");
		__.GetLogger()._EzError(bytes.Length >= start + count, "byte array out of bounds!!");

		int floatCount = count / 4;
		int bytesPosition = start;

		floats = new float[floatCount];
		for (int i = 0; i < floatCount; i++)
		{
			floats[i] = BitConverter.ToSingle(bytes, bytesPosition);
			bytesPosition += 4;
		}
	}

	/// <summary>
	///    convert a byte array to an int array
	/// </summary>
	/// <param name="bytes"></param>
	/// <param name="start"></param>
	/// <param name="count"></param>
	/// <param name="intArray"></param>
	public static void _ToArray(this byte[] bytes, int start, int count, out int[] intArray)
	{
		//__.ERROR.AssertOnce("add unitTest for endianness");

		__.GetLogger()._EzError(count % 4 == 0, "count should be multiple of 4!!");
		__.GetLogger()._EzError(bytes.Length >= start + count, "byte array out of bounds!!");

		int floatCount = count / 4;
		int bytesPosition = start;

		intArray = new int[floatCount];
		for (int i = 0; i < floatCount; i++)
		{
			intArray[i] = BitConverter.ToInt32(bytes, bytesPosition);
			bytesPosition += 4;
		}
	}

	/// <summary>
	///    compare 2 arrays
	/// </summary>
	/// <param name="thisArray"></param>
	/// <param name="toCompare"></param>
	/// <param name="thisStartPosition"></param>
	/// <param name="compareStartPosition"></param>
	/// <param name="length"></param>
	/// <returns></returns>
	public static int _Compare(this byte[] thisArray, byte[] toCompare, int thisStartPosition, int compareStartPosition,
		int length)
	{
		var thisPos = thisStartPosition;
		var comparePos = compareStartPosition;

		if (length == -1)
		{
			length = toCompare.Length;
		}

		for (int i = 0; i < length; i++)
		{
			if (thisArray[thisPos] != toCompare[comparePos])
			{
				return toCompare[comparePos] - thisArray[thisPos];
			}

			thisPos++;
			comparePos++;
		}

		return 0;
	}

	public static int _Compare(this byte[] thisArray, byte[] toCompare)
	{
		return thisArray._Compare(toCompare, 0, 0, -1);
	}

	public static void _ToArray(this byte[] bytes, int start, int count, out short[] shorts)
	{
		__.GetLogger()._EzError(count % sizeof(short) == 0, "count should be multiple of shorts!!");
		__.GetLogger()._EzError(bytes.Length >= start + count, "byte array out of bounds!!");

		int shortCount = count / sizeof(short);
		int bytesPosition = start;

		shorts = new short[shortCount];
		for (int i = 0; i < shortCount; i++)
		{
			shorts[i] = BitConverter.ToInt16(bytes, bytesPosition);
			bytesPosition += sizeof(short);
		}
	}


	//public static void _Compress(this byte[] inputUncompressed, ref byte[] resizableOutputTarget, out int outputLength)
	//{
	//	inputUncompressed._Compress(0, inputUncompressed.Length, ref resizableOutputTarget, out outputLength);
	//}
	////[Placeholder("need snappy instead of this low perf zip stuff")]
	//[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
	//public static void _Compress(this byte[] inputUncompressed, int offset, int count, ref byte[] resizableOutputTarget, out int outputLength)
	//{
	//	//stupid Ionic.Zlib way, very object and performance wastefull
	//	{
	//		using (var ms = new MemoryStream())
	//		{
	//			Stream compressor =
	//				new ZlibStream(ms, CompressionMode.Compress, CompressionLevel.BestSpeed);

	//			using (compressor)
	//			{
	//				compressor.Write(inputUncompressed, offset, count);

	//			}

	//			resizableOutputTarget = ms.ToArray();
	//			outputLength = (int)resizableOutputTarget.Length;
	//		}
	//	}
	//	//below should work, but doesn't because of stupid implementation of Ionic.Zlib not allowing object reuse
	//	{

	//		//object obj;
	//		//if (!updateState.Tags.TryGetValue("Novaleaf.Byte[].Compress", out obj))
	//		//{
	//		//   obj = new Ionic.Zlib.ZlibStream(new MemoryStream(), Ionic.Zlib.CompressionMode.Compress,
	//		//                                   Ionic.Zlib.CompressionLevel.BestSpeed);
	//		//   updateState.Tags.Add("Novaleaf.Byte[].Compress", obj);
	//		//}
	//		//var compressor = obj as Ionic.Zlib.ZlibStream;
	//		////reset our stream
	//		//compressor._baseStream.SetLength(0);
	//		//compressor.Write(inputUncompressed, offset, count);

	//		//compressor.Flush();

	//		//compressor.Close();

	//		//var memoryStream = compressor._baseStream._stream as MemoryStream;
	//		//__.GetLogger()._EzError(memoryStream.Length < int.MaxValue / 2, "output too big!");

	//		//outputLength = (int)memoryStream.Length;
	//		//outputCompressed_TEMP_SCRATCH = memoryStream.GetBuffer();
	//	}
	//}

	//////public static void Compress(this byte[] inputUncompressed, int offset, int count, FrameState updateState, out byte[] outputCompressed_TEMP_SCRATCH, out int outputLength)
	//////{
	//////   updateState.AssertIsAlive();
	//////   //stupid Ionic.Zlib way, very object and performance wastefull
	//////   {
	//////      using (var ms = new MemoryStream())
	//////      {
	//////         Stream compressor =
	//////            new Ionic.Zlib.ZlibStream(ms, Ionic.Zlib.CompressionMode.Compress, Ionic.Zlib.CompressionLevel.BestSpeed);

	//////         using (compressor)
	//////         {
	//////            compressor.Write(inputUncompressed, offset, count);

	//////         }
	//////         outputCompressed_TEMP_SCRATCH = ms.ToArray();
	//////         outputLength = (int)outputCompressed_TEMP_SCRATCH.Length;
	//////      }
	//////   }
	//////   //below should work, but doesn't because of stupid implementation of Ionic.Zlib not allowing object reuse
	//////   {

	//////      //object obj;
	//////      //if (!updateState.Tags.TryGetValue("Novaleaf.Byte[].Compress", out obj))
	//////      //{
	//////      //   obj = new Ionic.Zlib.ZlibStream(new MemoryStream(), Ionic.Zlib.CompressionMode.Compress,
	//////      //                                   Ionic.Zlib.CompressionLevel.BestSpeed);
	//////      //   updateState.Tags.Add("Novaleaf.Byte[].Compress", obj);
	//////      //}
	//////      //var compressor = obj as Ionic.Zlib.ZlibStream;
	//////      ////reset our stream
	//////      //compressor._baseStream.SetLength(0);
	//////      //compressor.Write(inputUncompressed, offset, count);

	//////      //compressor.Flush();

	//////      //compressor.Close();

	//////      //var memoryStream = compressor._baseStream._stream as MemoryStream;
	//////      //__.GetLogger()._EzError(memoryStream.Length < int.MaxValue / 2, "output too big!");

	//////      //outputLength = (int)memoryStream.Length;
	//////      //outputCompressed_TEMP_SCRATCH = memoryStream.GetBuffer();
	//////   }
	//////}


	//	/// <summary>
	//	/// decompress bytes that were compressed using our <see cref="Compress"/> method.   
	//	/// if fails (data corruption, etc), the returning false and <see cref="outputLength"/> -1
	//	/// </summary>
	//	/// <param name="inputCompressed"></param>
	//	/// <param name="offset"></param>
	//	/// <param name="count"></param>
	//	/// <param name="updateState"></param>
	//	/// <param name="outputUncompressed_TEMP_SCRATCH"></param>
	//	/// <param name="outputLength"></param>
	//	public static bool TryDecompress(this byte[] inputCompressed, ref byte[] resizableOutputTarget, out int outputLength)
	//	{
	//		return inputCompressed.TryDecompress(0, inputCompressed.Length, ref resizableOutputTarget, out outputLength);
	//	}


	//	/// <summary>
	//	/// decompress bytes that were compressed using our <see cref="Compress"/> method.   
	//	/// if fails (data corruption, etc), the returning false and <see cref="outputLength"/> -1
	//	/// </summary>
	//	/// <param name="inputCompressed"></param>
	//	/// <param name="offset"></param>
	//	/// <param name="count"></param>
	//	/// <param name="updateState"></param>
	//	/// <param name="outputUncompressed_TEMP_SCRATCH"></param>
	//	/// <param name="outputLength"></param>
	//	[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "offset"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "count"),]
	//	[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
	//	//[Placeholder("need snappy instead of this low perf zip stuff")]
	//	public static bool TryDecompress(this byte[] inputCompressed, int offset, int count, ref byte[] resizableOutputTarget, out int outputLength)
	//	{
	//		//stupid Ionic.Zlib way, very object and performance wastefull
	//		{
	//			using (var input = new MemoryStream(inputCompressed))
	//			{
	//				Stream decompressor =
	//					new ZlibStream(input, CompressionMode.Decompress);

	//				// workitem 8460
	//				byte[] working = new byte[1024];
	//				using (var output = new MemoryStream())
	//				{
	//					using (decompressor)
	//					{
	//						int n;
	//						while ((n = decompressor.Read(working, 0, working.Length)) != 0)
	//						{
	//							if (n == ZlibConstants.Z_DATA_ERROR)
	//							{
	//								//error with output
	//#if DEBUG
	//								if (resizableOutputTarget != null)
	//								{
	//									resizableOutputTarget.Clear();
	//								}
	//#endif
	//								outputLength = -1;
	//								return false;
	//							}
	//							output.Write(working, 0, n);
	//						}
	//					}

	//					resizableOutputTarget = output.ToArray();
	//					outputLength = resizableOutputTarget.Length;
	//					return true;
	//				}
	//			}
	//		}
	//		//below should work, but doesn't because of stupid implementation of Ionic.Zlib not allowing object reuse
	//		{

	//			//object obj;
	//			//if (!updateState.Tags.TryGetValue("Novaleaf.Byte[].Decompress", out obj))
	//			//{
	//			//   obj = new Ionic.Zlib.ZlibStream(new MemoryStream(), Ionic.Zlib.CompressionMode.Decompress);

	//			//   updateState.Tags.Add("Novaleaf.Byte[].Decompress", obj);
	//			//}
	//			//var decompressor = obj as Ionic.Zlib.ZlibStream;

	//			//decompressor._baseStream.SetLength(0);
	//			//decompressor.Write(inputCompressed, offset, count);

	//			//decompressor.Flush();

	//			//var memoryStream = decompressor._baseStream._stream as MemoryStream;
	//			//__.GetLogger()._EzError(memoryStream.Length < int.MaxValue / 2, "output too big!");


	//			//outputLength = (int)memoryStream.Length;
	//			//outputUncompressed_TEMP_SCRATCH = memoryStream.GetBuffer();
	//		}

	//	}


	//	///// <summary>
	//	///// decompress bytes that were compressed using our <see cref="Compress"/> method.   
	//	///// if fails (data corruption, etc), the returning <see cref="outputUncompressed_TEMP_SCRATCH"/> will be null and <see cref="outputLength"/> -1
	//	///// </summary>
	//	///// <param name="inputCompressed"></param>
	//	///// <param name="offset"></param>
	//	///// <param name="count"></param>
	//	///// <param name="updateState"></param>
	//	///// <param name="outputUncompressed_TEMP_SCRATCH"></param>
	//	///// <param name="outputLength"></param>
	//	//public static void Decompress(this byte[] inputCompressed, int offset, int count, FrameState updateState, out byte[] outputUncompressed_TEMP_SCRATCH, out int outputLength)
	//	//{
	//	//   updateState.AssertIsAlive();

	//	//   //stupid Ionic.Zlib way, very object and performance wastefull
	//	//   {
	//	//      using (var input = new MemoryStream(inputCompressed))
	//	//      {
	//	//         Stream decompressor =
	//	//            new Ionic.Zlib.ZlibStream(input, Ionic.Zlib.CompressionMode.Decompress);

	//	//         // workitem 8460
	//	//         byte[] working = new byte[1024];
	//	//         using (var output = new MemoryStream())
	//	//         {
	//	//            using (decompressor)
	//	//            {
	//	//               int n;
	//	//               while ((n = decompressor.Read(working, 0, working.Length)) != 0)
	//	//               {
	//	//                  if (n == Ionic.Zlib.ZlibConstants.Z_DATA_ERROR)
	//	//                  {
	//	//                     //error with output
	//	//                     outputUncompressed_TEMP_SCRATCH = null;
	//	//                     outputLength = -1;
	//	//                  }
	//	//                  output.Write(working, 0, n);
	//	//               }
	//	//            }
	//	//            outputUncompressed_TEMP_SCRATCH = output.GetBuffer();
	//	//            outputLength = (int)output.Length;
	//	//         }

	//	//      }
	//	//   }
	//	//   //below should work, but doesn't because of stupid implementation of Ionic.Zlib not allowing object reuse
	//	//   {

	//	//      //object obj;
	//	//      //if (!updateState.Tags.TryGetValue("Novaleaf.Byte[].Decompress", out obj))
	//	//      //{
	//	//      //   obj = new Ionic.Zlib.ZlibStream(new MemoryStream(), Ionic.Zlib.CompressionMode.Decompress);

	//	//      //   updateState.Tags.Add("Novaleaf.Byte[].Decompress", obj);
	//	//      //}
	//	//      //var decompressor = obj as Ionic.Zlib.ZlibStream;

	//	//      //decompressor._baseStream.SetLength(0);
	//	//      //decompressor.Write(inputCompressed, offset, count);

	//	//      //decompressor.Flush();

	//	//      //var memoryStream = decompressor._baseStream._stream as MemoryStream;
	//	//      //__.GetLogger()._EzError(memoryStream.Length < int.MaxValue / 2, "output too big!");


	//	//      //outputLength = (int)memoryStream.Length;
	//	//      //outputUncompressed_TEMP_SCRATCH = memoryStream.GetBuffer();
	//	//   }

	//	//}
}

