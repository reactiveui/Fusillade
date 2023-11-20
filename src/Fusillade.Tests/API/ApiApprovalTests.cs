// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

namespace Fusillade.APITests
{
    /// <summary>
    /// Tests for handling API approval.
    /// </summary>
    [ExcludeFromCodeCoverage]
    [UsesVerify]
    public class ApiApprovalTests
    {
        /// <summary>
        /// Tests to make sure the akavache project is approved.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Fact]
        public Task FusilladeTests() => typeof(OfflineHttpMessageHandler).Assembly.CheckApproval(["Fusillade"]);
    }
}
