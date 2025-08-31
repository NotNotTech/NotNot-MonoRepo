using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotNot.Serialization;

/// <summary>
///    wrapper over JsonConverter to easily convert weird c# objects to string.
///    Only implements writing.
///    Used internal to SerializationHelper
/// </summary>
/// <typeparam name="T"></typeparam>
public class ObjConverter<T> : JsonConverter<T>
{
	/// <summary>
	///    by default, we say types match if the Type is assignable to T.   If you want to be more strict (exact match only),
	///    set this to true.
	/// </summary>
	private bool _exactTypeOnly;

	public ObjConverter(Func<T, object?> write, bool exactTypeOnly = false)
	{
		DoWrite = write;
		_exactTypeOnly = exactTypeOnly;
	}

	public Func<T, object?> DoWrite { get; init; }

	public override bool CanConvert(Type typeToConvert)
	{
		if (_exactTypeOnly)
		{
			if (typeToConvert == typeof(T))
			{
				return true;
			}
		}
		else
		{
			if (typeToConvert.IsAssignableTo(typeof(T)))
			{
				return true;
			}
		}

		return false;
	}

	public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		throw new NotImplementedException("This is only for converting weird C# objects to string for logging display.");
	}

	public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
	{
		//WriteAsString((writer, value, options));
		var result = DoWrite(value);

		switch (result)
		{
			case string str:
				writer.WriteStringValue(str);
				return;
			case null:
				writer.WriteNullValue();
				return;
		}


		//writer.WriteStartObject();//bug to use.  not needed as the following recursive write adds an object wrapper for us.
		{
			//recursive write
			JsonSerializer.Serialize(writer, result, options);
		}
		//writer.WriteEndObject();
	}
}

public class RoundtripObjConverter<T> : JsonConverter<T>, IDisposable
{

	public delegate T? ReaderAction(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);
	public delegate void WriterAction(Utf8JsonWriter writer, T value, JsonSerializerOptions options);


	private WriterAction DoWrite;
	private ReaderAction DoRead;

	public RoundtripObjConverter(WriterAction writeAction, ReaderAction readAction)
	{
		DoWrite = writeAction;
		DoRead = readAction;
	}


	public override bool CanConvert(Type typeToConvert)
	{
		if (typeToConvert == typeof(T))
		{
			return true;
		}

		return false;
	}

	public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return DoRead(ref reader, typeToConvert, options);
	}

	public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
	{
		DoWrite(writer, value, options);
		writer.Dispose();
	}

	public void Dispose()
	{
		DoRead = null;
		DoWrite = null;
	}


}
