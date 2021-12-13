// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using PublicApiGenerator;
using VerifyXunit;

namespace Fusillade.APITests
{
    /// <summary>
    /// Api Approval Base.
    /// </summary>
    [ExcludeFromCodeCoverage]
    [UsesVerify]
    public abstract class ApiApprovalBase
    {
        /// <summary>
        /// Check Approval.
        /// </summary>
        /// <param name="assembly">The assembly.</param>
        /// <param name="filePath">The file path.</param>
        /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
        protected static Task CheckApproval(Assembly assembly, [CallerFilePath] string? filePath = null)
        {
            if (filePath is null)
            {
                return Task.CompletedTask;
            }

            var generatorOptions = new ApiGeneratorOptions { WhitelistedNamespacePrefixes = new[] { "Fusillade" } };
            var apiText = assembly.GeneratePublicApi(generatorOptions);
            return Verifier.Verify(apiText, null, filePath)
                .UniqueForRuntimeAndVersion()
                .ScrubEmptyLines()
                .ScrubLines(l =>
                    l.StartsWith("[assembly: AssemblyVersion(", StringComparison.InvariantCulture) ||
                    l.StartsWith("[assembly: AssemblyFileVersion(", StringComparison.InvariantCulture) ||
                    l.StartsWith("[assembly: AssemblyInformationalVersion(", StringComparison.InvariantCulture) ||
                    l.StartsWith("[assembly: System.Reflection.AssemblyMetadata(", StringComparison.InvariantCulture));
        }
    }
}
