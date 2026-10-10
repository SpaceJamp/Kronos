namespace Kronos.Data;

public record DLLImportResult
{
    public bool Success { get; private set; }
    public string FilePath { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public bool ImportedAsDownload { get; private set; }

    /// <summary>
    /// Set when the file was accepted without its signature being checked, because the platform
    /// has no Authenticode implementation.
    /// </summary>
    /// <remarks>
    /// The import still succeeded. This exists so the caller can tell the user that the one check
    /// Windows performs did not happen, rather than showing a plain green "imported" for a file that
    /// was never verified. Always false on Windows.
    /// </remarks>
    public bool SignatureNotVerified { get; private set; }

    /// <summary>The message to show when <see cref="SignatureNotVerified"/> is set.</summary>
    public string SignatureNotVerifiedMessage { get; private set; } = string.Empty;

    private DLLImportResult()
    {

    }

    public static DLLImportResult FromSucces(string filePath, string message, bool importedAsDownload)
    {
        var dllImportResult = new DLLImportResult()
        {
            Success = true,
            FilePath = filePath,
            ImportedAsDownload = importedAsDownload,
            Message = message,
        };
        return dllImportResult;
    }

    public static DLLImportResult FromFail(string filePath, string message)
    {
        var dllImportResult = new DLLImportResult()
        {
            Success = false,
            FilePath = filePath,
            Message = message,
        };
        return dllImportResult;
    }

    /// <summary>
    /// Records that this import was not signature-checked, so the UI can warn about it.
    /// </summary>
    /// <param name="result">The successful result to annotate.</param>
    /// <param name="warning">The user-facing warning text.</param>
    /// <returns>The same instance, for chaining at the return site.</returns>
    /// <remarks>
    /// Mutates and returns rather than taking a copy, because the callers annotate immediately
    /// before returning and every other method here already hands back the instance it built.
    /// </remarks>
    public DLLImportResult WithSignatureNotVerified(string warning)
    {
        SignatureNotVerified = true;
        SignatureNotVerifiedMessage = warning;
        return this;
    }

    public override string ToString()
    {
        return $"Success: {Success}, FilePath: {FilePath}, ImportedAsDownload: {ImportedAsDownload}, SignatureNotVerified: {SignatureNotVerified}, Message: {Message}";
    }
}