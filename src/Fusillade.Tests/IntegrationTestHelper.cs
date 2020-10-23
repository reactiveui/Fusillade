// Copyright (c) 2019 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text;

namespace Fusillade.Tests
{
    /// <summary>
    /// A helper for performing integration tests.
    /// </summary>
    public static class IntegrationTestHelper
    {
        /// <summary>
        /// Combines together paths together and then gets a full path.
        /// </summary>
        /// <param name="paths">The paths to combine.</param>
        /// <returns>The string path.</returns>
        public static string GetPath(params string[] paths)
        {
            var ret = GetIntegrationTestRootDirectory();
            return new FileInfo(paths.Aggregate(ret, Path.Combine)).FullName;
        }

        /// <summary>
        /// Gets the root directory for the integration test.
        /// </summary>
        /// <returns>The path.</returns>
        public static string GetIntegrationTestRootDirectory()
        {
            // XXX: This is an evil hack, but it's okay for a unit test
            // We can't use Assembly.Location because unit test runners love
            // to move stuff to temp directories
            return Directory.GetParent(Directory.GetCurrentDirectory()).Parent.FullName;
        }

        /// <summary>
        /// Creates a response from a sample file with the data.
        /// </summary>
        /// <param name="paths">The path to the file.</param>
        /// <returns>The generated response.</returns>
        public static HttpResponseMessage GetResponse(params string[] paths)
        {
            var bytes = File.ReadAllBytes(GetPath(paths));

            // Find the body
            var bodyIndex = -1;
            for (bodyIndex = 0; bodyIndex < bytes.Length - 3; bodyIndex++)
            {
                if (bytes[bodyIndex] != 0x0D || bytes[bodyIndex + 1] != 0x0A ||
                    bytes[bodyIndex + 2] != 0x0D || bytes[bodyIndex + 3] != 0x0A)
                {
                    continue;
                }

                goto foundIt;
            }

            throw new Exception("Couldn't find response body");

        foundIt:

            var headerText = Encoding.UTF8.GetString(bytes, 0, bodyIndex);
            var lines = headerText.Split('\n');
            var statusCode = (HttpStatusCode)int.Parse(lines[0].Split(' ')[1]);
            var ret = new HttpResponseMessage(statusCode);

            ret.Content = new ByteArrayContent(bytes, bodyIndex + 2, bytes.Length - bodyIndex - 2);

            foreach (var line in lines.Skip(1))
            {
                var separatorIndex = line.IndexOf(':');
                var key = line.Substring(0, separatorIndex);
                var val = line.Substring(separatorIndex + 2).TrimEnd();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                ret.Headers.TryAddWithoutValidation(key, val);
                ret.Content.Headers.TryAddWithoutValidation(key, val);
            }

            return ret;
        }
    }
}