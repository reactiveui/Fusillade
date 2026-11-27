// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Security.Cryptography;

#if REACTIVE_SHIM
namespace Fusillade.Reactive;
#else
namespace Fusillade;
#endif

/// <summary>Hashes request key text with SHA-256, which gives the same result in every process.</summary>
internal static class RequestKeyHash
{
#if !NET5_0_OR_GREATER
    /// <summary>The number of hex characters that encode one byte.</summary>
    private const int HexCharsPerByte = 2;
#endif

    /// <summary>Returns the SHA-256 hash of the UTF-8 bytes of <paramref name="text"/> as lowercase hex.</summary>
    /// <param name="text">The text to hash.</param>
    /// <returns>64 lowercase hex characters.</returns>
    internal static string ToLowerHex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
#if NET5_0_OR_GREATER
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
#else
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        var hex = new StringBuilder(hash.Length * HexCharsPerByte);
        foreach (var value in hash)
        {
            _ = hex.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return hex.ToString();
#endif
    }
}
