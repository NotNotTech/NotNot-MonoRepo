//using Xunit;
using Isopoh.Cryptography.Argon2;
using System.Text;
using System.Security.Cryptography;
using System.Security;
//using Xunit.Abstractions;
using System.Net.Sockets;
using System.Diagnostics;

namespace NotNot.Security;

/// <summary>
/// Key Derivation Function utilizing Argon2 internally.
/// creates and verifies one-way hashes of secrets/passwords.
/// <para>This custom implementation is backwards (and forwards) compatible with basic argon2, but it also includes optimizations to ignore computation of "blatently false" passwords.
/// This occurs by utilizing taking the kdf of the password's sha512 hash, and also storing the first 4 bytes of the sha512 in plaintext.   
/// If during verification the first 4 bytes don't match, the kdf does not need to be performed and instead just waits</para>
/// </summary>
/// <remarks><para>see https://en.wikipedia.org/wiki/Key_derivation_function</para></remarks>
public static class Kdf
{
	/// <summary>
	/// verify that the pw matches the existing kdfHashedPassword
	/// </summary>
	public static bool VerifyOld(string password, string kdfHashedPassword, bool isPwBase64PrehashedSha512 = false)
	{
		if (isPwBase64PrehashedSha512 is false)
		{
			var bytes = password._ToBytes();
			var hashBytes = SHA512.HashData(bytes);
			password = Convert.ToBase64String(hashBytes);
		}

		return Argon2.Verify(kdfHashedPassword, password);
	}

	public static async Task<bool> Verify(string password, string kdfHashedPassword, bool isPwBase64PrehashedSha512 = false, TimeSpan minDelay = default)
	{
		if (minDelay == default)
		{
			minDelay = TimeSpan.FromSeconds(0.5);
		}
		var sw = Stopwatch.StartNew();
		try
		{

			_GetPwBytes(password, isPwBase64PrehashedSha512, out var pwSha512Bytes);//, out var hash4);


			//these various ways below to inject metadata don't work with argon2 because it expects to exactly control the kdfHash.
			//so disabling for now, will try to do "early rejection" later, one fully moved to .net

			////////the kdfHashedPassword contains metadata.
			//////////add in custom metadata containing first 4 digits of pwSha512 for "early rejection"
			////////so that when .Verify() is called, if the first 4 digits of input pwSha512 doesn't match the hash4,
			////////we can reject and wait idle instead of computing the argon2 hash.
			////////https://github.com/simonepri/phc-format
			///
			////////var phcFormat = kdfHashedPassword.Split('$', StringSplitOptions.TrimEntries);
			//////if (kdfHashedPassword.Contains("$hash4="))
			//////{
			//////   var remainder = kdfHashedPassword._GetAfter("$hash4=",false);
			//////   var expectedHash = remainder._GetBefore('$', true);

			//////   //var phcParams = phcFormat[3].Split(',');
			//////   //var hashParam = phcParams.First(param => param.Contains("hash4="));
			//////   //var pair = hashParam.Split('=');
			//////   //var key = pair[0];
			//////   //var expectedHash = pair[1];

			//////   if (expectedHash != hash4)
			//////   {
			//////      //expected first 4 digits do not match.   early-abort argon2 calculations
			//////      return false;
			//////   }
			//////}

			//do normal argon2 verification

			//var config = new Argon2Config();
			//config.DecodeString(kdfHashedPassword,out _);

			//config.Password = pwSha512Bytes;

			//var result1 = Argon2.Verify(kdfHashedPassword, config);




			return Argon2.Verify(kdfHashedPassword, pwSha512Bytes);

		}
		finally
		{
			var elapsed = sw.Elapsed;

			var remainingDelay = minDelay - elapsed;
			if (remainingDelay >= TimeSpan.Zero)
			{
				await Task.Delay(remainingDelay);
			}
			else
			{
				//already exceeded, wait so that our total time is an increment of minDelay
				var delayInterval = minDelay + remainingDelay._Mod(minDelay);
				await Task.Delay(delayInterval);
			}
		}
	}

	/// <summary>
	/// create a argon2OutputDigest for the given password
	/// </summary>
	public static string Hash(string password, bool isPwBase64PrehashedSha512 = false)
	{
		_GetPwBytes(password, isPwBase64PrehashedSha512, out var pwSha512Bytes); //, out var hash4);

		var salt = RandomNumberGenerator.GetBytes(16);
		var config = new Argon2Config
		{
			Password = pwSha512Bytes,
			Type = Argon2Type.HybridAddressing,
			Version = Argon2Version.Nineteen,
			TimeCost = 10,
			MemoryCost = 65536,
			Lanes = 4,
			Threads = 1,
			Salt = salt,
			HashLength = 32,
		};

		var kdfHashedPassword = Argon2.Hash(config);

		return kdfHashedPassword;

		//these various ways below to inject metadata don't work with argon2 because it expects to exactly control the kdfHash.
		//so disabling for now, will try to do "early rejection" later, one fully moved to .net

		//////the kdfHashedPassword contains metadata.
		////////add in custom metadata containing first 4 digits of pwSha512 for "early rejection"
		//////so that when .Verify() is called, if the first 4 digits of input pwSha512 doesn't match the hash4,
		//////we can reject and wait idle instead of computing the argon2 hash.
		//////https://github.com/simonepri/phc-format

		////var kdfHashedPasswordWithHash4 = $"hash4={hash4},kdf={kdfHashedPassword}";

		//////var phcFormat = kdfHashedPassword.Split('$');
		////////params
		////////phcFormat[3] += $",hash4={hash4}";
		//////var kdfHashedPasswordWithHash4 = string.Join('$', phcFormat);



		//////var testSplit = toReturn.Split('$');
		//////testSplit[3] += ",nn=1";
		//////var testConfig = string.Join('$',testSplit);

		//////var config2 = new Argon2Config();
		//////config2.DecodeString(toReturn, out var hash2);

		//////var config3 = new Argon2Config();
		//////config2.DecodeString(testConfig, out var hash3);


		////return kdfHashedPasswordWithHash4;
	}

	private static void _GetPwBytes(string password, bool isPwBase64PrehashedSha512, out byte[] pwBase64EncodedSha512Bytes)//, out string hash4)
	{
		//byte[] pwSha512Bytes;
		if (isPwBase64PrehashedSha512)
		{
			//pwSha512Base64Bytes = password._FromBase64();//  Convert.FromBase64String(password._b);         
			pwBase64EncodedSha512Bytes = password._ToBytes();
		}
		else
		{
			var bytes = password._ToBytes();
			var pwSha512Bytes = SHA512.HashData(bytes);
			var pwSha512Base64 = pwSha512Bytes._ToBase64();
			pwBase64EncodedSha512Bytes = pwSha512Base64._ToBytes();
		}

		//if (pwSha512Bytes.Length != 64)
		//{
		//   throw new SecurityException($"hash is expected to be 64 bytes but is {pwSha512Bytes.Length}");
		//}
		//hash4 = ((ReadOnlySpan<byte>)pwSha512Bytes).Slice(0, 4).ToArray()._ToBase64();
	}


	//public class Tests
	//{
	//   /// <summary>
	//   /// verify that pjsc legacy hash can be verified properly
	//   /// </summary>
	//   [Fact]
	//   internal async Task LegacyVerify()
	//   {
	//      //a pjsc legacy argon2 digest and it's respective pw
	//      //var argon2OutputDigest = "$argon2i$m=4096,t=10,p=2$PULvv73vv71q77+977+9Hw$lahPtyP8+75QmphI4I66asTVf5nlXHiwr5itJsN/Q2E";
	//      var argon2OutputDigest = "$argon2i$m=4096,t=10,p=2$PULvv73vv71q77+977+9Hw$lahPtyP8+75QmphI4I66asTVf5nlXHiwr5itJsN/Q2E";
	//      var pw = "secret duck";

	//      var result = await Verify(pw, argon2OutputDigest);
	//      var result2 = VerifyOld(pw, argon2OutputDigest);

	//      __.Assert(result);


	//      var config = new Argon2Config();
	//      config.DecodeString(argon2OutputDigest, out var hash);
	//      __.Assert(config.Salt is not null, "assumption that legacy use salt");
	//   }

	//   [Fact]
	//   internal async Task BasicHash()
	//   {

	//      var pw = "secret duck";

	//      var digest = Hash(pw);
	//      var result = await Verify(pw, digest);
	//      __.Assert(result);

	//      var result2 = await Verify(pw, "$argon2id$v=19$m=65536,t=3,p=1$wj+z7XEcaMUPZpA8/KNj5w$C3g2qHxgXH52x2ndoYcq0LtkyiGJdPBWNTb8riXM9y0");
	//      __.Assert(result2);

	//      var config = new Argon2Config();
	//      config.DecodeString(digest, out var hash);
	//      __.Assert(config.Salt is not null, "assumption that modern uses random salt");

	//      var digest2 = Hash(pw);
	//      var config2 = new Argon2Config();
	//      config2.DecodeString(digest2, out var hash2);
	//      __.Assert(config2.Salt is not null && config2.Salt.SequenceEqual(config.Salt) is false, "assumption that modern uses random salt");




	//   }


	//}



}
