using System;
using System.IO;
using System.Security.Cryptography;

namespace Kronos.Extensions;

internal static class FileStreamExtensions
{
    internal static string GetMD5Hash(this Stream fileStream)
    {
        fileStream.Position = 0;

        using (var md5 = MD5.Create())
        {
            var hash = md5.ComputeHash(fileStream);
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }
    }

    /// <summary>
    /// SHA256 of a stream, upper-case hex, which is the form GitHub publishes asset digests in.
    /// </summary>
    /// <remarks>
    /// Used to verify the downloaded release archive against the digest in the release metadata, which is
    /// the gate on installing anything at all. Nearly removed as dead code on the strength of a grep that
    /// missed the call sites; the compiler caught it, which is the argument for having a compiler.
    /// </remarks>
    internal static string GetSha256Hash(this Stream fileStream)
    {
        fileStream.Position = 0;

        using (var sha256 = SHA256.Create())
        {
            var hash = sha256.ComputeHash(fileStream);
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }
    }
}
