namespace Clawleash.Models;

/// <summary>
/// サンドボックス内で実行されたコマンドの結果
/// </summary>
public class CommandResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public bool Success => ExitCode == 0;

    public CommandResult() { }

    public CommandResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public override string ToString()
    {
        if (Success)
        {
            return string.IsNullOrEmpty(StandardOutput)
                ? $"Exit code: {ExitCode}"
                : StandardOutput;
        }

        return $"Error (Exit code: {ExitCode}): {StandardError}";
    }
}
