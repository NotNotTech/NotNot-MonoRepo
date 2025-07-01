namespace NotNot;

/// <summary>
///    various conversion methods
/// </summary>
public class ConvertHelper
{
	public static ConvertHelper Instance = new();

	/// <summary>
	///    Convert an integer to a roman numeral string representation
	/// </summary>
	/// <remarks>from https://www.reddit.com/r/csharp/comments/11dvyw6/needed_a_converter_to_roman_numerals_in_c_google/</remarks>
	/// <param name="num"></param>
	/// <returns></returns>
	public string IntToRomanNumeral(int num)
	{
		return num switch
		{
			>= 1000 => "M" + IntToRomanNumeral(num - 1000),
			>= 900 => "CM" + IntToRomanNumeral(num - 900),
			>= 500 => "D" + IntToRomanNumeral(num - 500),
			>= 400 => "CD" + IntToRomanNumeral(num - 400),
			>= 100 => "C" + IntToRomanNumeral(num - 100),
			>= 90 => "XC" + IntToRomanNumeral(num - 90),
			>= 50 => "L" + IntToRomanNumeral(num - 50),
			>= 40 => "XL" + IntToRomanNumeral(num - 40),
			>= 10 => "X" + IntToRomanNumeral(num - 10),
			>= 9 => "IX" + IntToRomanNumeral(num - 9),
			>= 5 => "V" + IntToRomanNumeral(num - 5),
			>= 4 => "IV" + IntToRomanNumeral(num - 4),
			>= 1 => "I" + IntToRomanNumeral(num - 1),
			_ => string.Empty
		};
	}
}
