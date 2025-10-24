using System.Runtime.CompilerServices;
using Spectre.Console;

namespace NotNot;

/// <summary>
/// ez console based I/O for "operator" use.
/// </summary>
public class OperatorService : IDiSingletonService
{
	public bool Confirm(Color color, string message, bool defaultValue = false)
	{
		return Confirm(color._MarkupString(message), defaultValue);
	}

	public bool Confirm(string message, bool defaultValue = false)
	{
		var result = AnsiConsole.Confirm(message, defaultValue);
		return result;
	}

	public void WriteLine(Color color, string? message = null, object? arg0 = null, object? objToLog0 = null, object? objToLog1 = null,
		[CallerArgumentExpression("arg0")] string argName0 = "null",
		[CallerArgumentExpression("objToLog0")] string objToLog0Name = "null",
		[CallerArgumentExpression("objToLog1")] string objToLog1Name = "null")
	{
		//WriteLine( color._MarkupString(message), arg0, objToLog0, objToLog1, argName0, objToLog0Name, objToLog1Name);
		var coloredMessage = color._MarkupString(message);
		if (coloredMessage is not null)
		{
			var finalMessage = coloredMessage._FormatAppendArgs(arg0, objToLog0, objToLog1, argName0, objToLog0Name, objToLog1Name);
			AnsiConsole.MarkupLine(finalMessage);
		}
		else
		{
			AnsiConsole.WriteLine();
		}
	}
	public Color DefaultColor { get; set; } = Color.Orchid;
	public void WriteLine(string? message = null, object? arg0 = null, object? objToLog0 = null, object? objToLog1 = null,
		[CallerArgumentExpression("arg0")] string argName0 = "null",
		[CallerArgumentExpression("objToLog0")] string objToLog0Name = "null",
		[CallerArgumentExpression("objToLog1")] string objToLog1Name = "null")
	{
		WriteLine(DefaultColor, message, arg0, objToLog0, objToLog1, argName0, objToLog0Name, objToLog1Name);

		//if (message is not null)
		//{
		//   var finalMessage = message._FormatAppendArgs(arg0, objToLog0, objToLog1, argName0, objToLog0Name, objToLog1Name);
		//   AnsiConsole.MarkupLine(finalMessage);
		//}
		//else
		//{
		//   AnsiConsole.WriteLine();
		//}
	}
}
