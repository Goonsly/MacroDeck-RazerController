namespace KeystoneDigital.RazerController.Autostart;

/// <summary>Token-level edits to the RazerAppEngine Run command line, so a flag is matched whole, never as a substring.</summary>
internal static class AutostartCommandLine
{
	private static (string ExecutableToken, string Remainder) Split(string commandLine)
	{
		if (string.IsNullOrEmpty(commandLine)) return ("", "");

		if (commandLine[0] == '"')
		{
			var closing = commandLine.IndexOf('"', 1);
			if (closing < 0) return (commandLine, ""); // unterminated quote: treat the whole thing as the token

			return (commandLine[..(closing + 1)], commandLine[(closing + 1)..]);
		}

		var spaceIndex = commandLine.IndexOf(' ');
		return spaceIndex < 0 ? (commandLine, "") : (commandLine[..spaceIndex], commandLine[spaceIndex..]);
	}

	public static string? ExtractExecutable(string commandLine)
	{
		var (token, _) = Split(commandLine);
		if (token.Length == 0) return null;

		return token.Length >= 2 && token[0] == '"' && token[^1] == '"'
			? token[1..^1]
			: token;
	}

	public static string ExtractArguments(string commandLine)
	{
		var (_, remainder) = Split(commandLine);
		return remainder.Length > 0 && remainder[0] == ' ' ? remainder[1..] : remainder;
	}

	public static bool HasFlag(string commandLine, string flag) => FindTokenIndex(commandLine, flag) >= 0;

	public static string AppendFlag(string commandLine, string flag)
	{
		if (HasFlag(commandLine, flag)) return commandLine;

		return commandLine.Length == 0 ? flag : commandLine + " " + flag;
	}

	public static string RemoveFlag(string commandLine, string flag)
	{
		var index = FindTokenIndex(commandLine, flag);
		if (index < 0) return commandLine;

		var before = commandLine[..index];
		var after = commandLine[(index + flag.Length)..];

		if (before.EndsWith(' '))
		{
			before = before[..^1];
		}
		else if (after.StartsWith(' '))
		{
			after = after[1..];
		}

		return before + after;
	}

	private static int FindTokenIndex(string commandLine, string flag)
	{
		if (string.IsNullOrEmpty(commandLine) || string.IsNullOrEmpty(flag)) return -1;

		var searchStart = 0;

		while (true)
		{
			var index = commandLine.IndexOf(flag, searchStart, StringComparison.Ordinal);
			if (index < 0) return -1;

			var precededByBoundary = index == 0 || commandLine[index - 1] == ' ';
			var followedByBoundary = index + flag.Length == commandLine.Length || commandLine[index + flag.Length] == ' ';

			if (precededByBoundary && followedByBoundary) return index;

			searchStart = index + 1;
		}
	}

	public static string ReplaceExecutable(string commandLine, string newExecutablePath)
	{
		var (token, remainder) = Split(commandLine);
		var wasQuoted = token.Length > 0 && token[0] == '"';
		var needsQuotes = wasQuoted || newExecutablePath.Contains(' ');
		var newToken = needsQuotes ? "\"" + newExecutablePath + "\"" : newExecutablePath;

		return newToken + remainder;
	}
}
