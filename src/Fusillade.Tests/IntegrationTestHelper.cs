// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>A helper for performing integration tests.</summary>
public static class IntegrationTestHelper
{
    /// <summary>The carriage-return byte (CR).</summary>
    private const byte CarriageReturn = 0x0D;

    /// <summary>The line-feed byte (LF).</summary>
    private const byte LineFeed = 0x0A;

    /// <summary>The number of bytes in a single CRLF sequence.</summary>
    private const int CrlfLength = 2;

    /// <summary>Combines together paths together and then gets a full path.</summary>
    /// <param name="paths">The paths to combine.</param>
    /// <returns>The string path.</returns>
    public static string GetPath(params string[] paths)
    {
        var ret = GetIntegrationTestRootDirectory();
        foreach (var path in paths)
        {
            ret = Path.Combine(ret, path);
        }

        return new FileInfo(ret).FullName;
    }

    /// <summary>Gets the root directory for the integration test.</summary>
    /// <returns>The path.</returns>
    public static string GetIntegrationTestRootDirectory()
    {
        // The test fixtures live next to the source, but at runtime the assembly is
        // executed from its build output (e.g. bin/<config>/<tfm>). We deliberately
        // avoid Assembly.Location here: many test runners shadow-copy or relocate the
        // assembly to a temp directory, which makes its reported location useless for
        // finding the fixtures. The current working directory, however, is the build
        // output folder, so walking up two levels (out of <tfm> and <config>) lands
        // back at the project directory where the sample files are checked in.
        var targetFrameworkDirectory = Directory.GetParent(Directory.GetCurrentDirectory());
        var configurationDirectory = targetFrameworkDirectory?.Parent;
        return configurationDirectory?.FullName ?? throw new InvalidOperationException("Could not locate the integration test root directory.");
    }

    /// <summary>Creates a response from a sample file with the data.</summary>
    /// <param name="paths">The path to the file.</param>
    /// <returns>The generated response.</returns>
    public static HttpResponseMessage GetResponse(params string[] paths)
    {
        var bytes = File.ReadAllBytes(GetPath(paths));

        // Find the body, separated from the headers by a CRLF CRLF sequence.
        var bodyIndex = FindBodyIndex(bytes);
        if (bodyIndex < 0)
        {
            throw new InvalidOperationException("Couldn't find response body");
        }

        var headerText = Encoding.UTF8.GetString(bytes, 0, bodyIndex);
        var lines = headerText.Split('\n');
        var statusCode = (HttpStatusCode)int.Parse(lines[0].Split(' ')[1]);
        var bodyStart = bodyIndex + CrlfLength;
        var ret = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(bytes, bodyStart, bytes.Length - bodyStart),
        };

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var separatorIndex = line.IndexOf(':');
            var key = line[0..(0 + separatorIndex)];
            var val = line.Substring(separatorIndex + CrlfLength).TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            _ = ret.Headers.TryAddWithoutValidation(key, val);
            _ = ret.Content.Headers.TryAddWithoutValidation(key, val);
        }

        return ret;
    }

    /// <summary>Finds the byte offset of the header/body separator (a double CRLF sequence).</summary>
    /// <param name="bytes">The raw response bytes.</param>
    /// <returns>The offset of the separator, or -1 if it is not present.</returns>
    private static int FindBodyIndex(byte[] bytes)
    {
        ReadOnlySpan<byte> separator = [CarriageReturn, LineFeed, CarriageReturn, LineFeed];
        return bytes.AsSpan().IndexOf(separator);
    }
}
