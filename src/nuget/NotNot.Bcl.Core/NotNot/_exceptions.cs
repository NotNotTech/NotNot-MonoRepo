using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace NotNot;
public class LoLoDiagnosticsException : LoLoException
{
	public LoLoDiagnosticsException()
	{
	}

	public LoLoDiagnosticsException(string message) : base(message)
	{
	}

	public LoLoDiagnosticsException(string message, Exception? innerException = null) : base(message, innerException)
	{
	}

	public LoLoDiagnosticsException(string message, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) : base(message)
	{
		Source = $"{memberName}:{sourceFilePath}:{sourceLineNumber}";
	}

	public LoLoDiagnosticsException(string message, Exception? innerException = null, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) : base(message, innerException)
	{
		Source = $"{memberName}:{sourceFilePath}:{sourceLineNumber}";
	}
}

public class LoLoException : Exception
{
	public LoLoException()
	{
	}

	public LoLoException(string message) : base(message)
	{
	}

	public LoLoException(string message, Exception? innerException = null) : base(message, innerException)
	{
	}
}
