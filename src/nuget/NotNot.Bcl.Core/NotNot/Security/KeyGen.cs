namespace NotNot.Security;

public static class KeyGen
{
   /// <summary>
   /// creates an apiKey in the legacy pjsc-api format.  
   /// <para>strongly prefer to use the non-legacy version in the future, as that is stronger</para>
   /// </summary>
   /// <param name="timestamp">if a timestamp should be used as start of key.  warning: this uses up 9 digits of the key.</param>
   /// <param name="randomDigits">must be minimum 20 digits as there is a deterministic (timestamp) component</param>
   /// <param name="digitGrouping"></param>
   /// <returns></returns>
   public static string CreateApiKey_Legacy(int randomDigits, int? digitGrouping)
   {
      __.ThrowIfNot(randomDigits >= 20, "must be minimum 20 digits as there is a deterministic (timestamp) component");

      var encoder = SimpleBase.Base32.Crockford;

      //improve legacy keygen to include timestamp to avoid key collisions
      bool timestamp = true;
      DateTime now = DateTime.UtcNow;
      // Convert strings and DateTime to byte arrays
      var nowLong = now.ToBinary()._Swizzle(); //scatter timestamp significant bits, makes resulting key less obvious to have a deterministic component
      byte[] nowBytes = timestamp ? BitConverter.GetBytes(nowLong) : [];

      var genByteLength = randomDigits;
      genByteLength -= nowBytes.Length;
      //we generate more bytes than we need, but that's ok we'll just always trim at the end.
      byte[] randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(genByteLength);

      //join nowBytes to randomBytes
      var nextInsertOffset = 0;
      var totalByteSize = nowBytes.Length + randomBytes.Length;
      byte[] totalBytes = new byte[totalByteSize];
      Buffer.BlockCopy(nowBytes, 0, totalBytes, nextInsertOffset, nowBytes.Length);
      nextInsertOffset += nowBytes.Length;
      Buffer.BlockCopy(randomBytes, 0, totalBytes, nextInsertOffset, randomBytes.Length);
      nextInsertOffset += randomBytes.Length;

      // Create a Span<byte>
      Span<byte> totalSpan = new Span<byte>(totalBytes, 0, nextInsertOffset);

      // span now contains the combined data

      //encode
      string encodedBytes = encoder.Encode(totalSpan).ToLowerInvariant();

      //check length
      if (encodedBytes.Length > randomDigits)
      {
         encodedBytes = encodedBytes.Substring(0, randomDigits);
      }
      __.AssertIfNot(encodedBytes.Length == randomDigits, "base58.Length == randomDigits");

      //group digits
      if (digitGrouping is not null)
      {
         var groups = encodedBytes._Split(digitGrouping.Value);
         encodedBytes = string.Join("-", groups);
      }
      return encodedBytes;




   }
   /// <summary>
   /// generate a cryptographically secure random string of alphanumeric characters (base58.Bitcoin encoded bytes).  useful for api keys, etc.
   /// <para>by default, includes timestamp data to help prevent collisions, but they are still theoretically possible so take care</para>
   /// </summary>
   /// <param name="randomDigits">must be minimum 15 digits as there is a deterministic (timestamp) component</param>
   /// <param name="digitGrouping"></param>
   /// <param name="timestamp">include a timestamp in the output to help reduce collisions</param>
   /// <returns></returns>
   public static string CreateApiKey(int randomDigits = 25, int? digitGrouping = 5, bool timestamp = true)
   {
      __.ThrowIfNot(randomDigits >= 15, "must be minimum 15 digits as there is a deterministic (timestamp) component");
      //switch to base58 as it provides same protections as base32, but also uses capital letters
      //var encoder = SimpleBase.Base32.Crockford;
      var encoder = SimpleBase.Base58.Bitcoin;

      DateTime now = DateTime.UtcNow;

      // Convert strings and DateTime to byte arrays
      var nowLong = now.ToBinary()._Swizzle(); //scatter timestamp significant bits, makes resulting key less obvious to have a deterministic component
      byte[] nowBytes = timestamp ? BitConverter.GetBytes(nowLong) : [];
      var genByteLength = randomDigits;
      genByteLength -= nowBytes.Length;

      //we only need aprox 73% of bytes to generate a base58 output of the same character length
      //for simplicity we generate more bytes than we need, but that's ok we'll just always trim at the end.
      byte[] randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(genByteLength);

      //join nowBytes to randomBytes
      var nextInsertOffset = 0;
      var totalByteSize = nowBytes.Length + randomBytes.Length;
      byte[] totalBytes = new byte[totalByteSize];
      Buffer.BlockCopy(nowBytes, 0, totalBytes, nextInsertOffset, nowBytes.Length);
      nextInsertOffset += nowBytes.Length;
      Buffer.BlockCopy(randomBytes, 0, totalBytes, nextInsertOffset, randomBytes.Length);
      nextInsertOffset += randomBytes.Length;

      // Create a Span<byte>
      Span<byte> totalSpan = new Span<byte>(totalBytes, 0, nextInsertOffset);

      // span now contains the combined data
      string encodedBytes = encoder.Encode(totalSpan);

      //encodedBytes = encodedBytes.ToLowerInvariant();

      //check length
      if (encodedBytes.Length > randomDigits)
      {
         encodedBytes = encodedBytes.Substring(0, randomDigits);
      }
      __.AssertIfNot(encodedBytes.Length == randomDigits);

      //group digits
      if (digitGrouping is not null)
      {
         var groups = encodedBytes._Split(digitGrouping.Value);
         encodedBytes = string.Join("-", groups);
      }
      return encodedBytes;
   }

}
