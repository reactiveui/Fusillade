// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace Fusillade.APITests;

/// <summary>
/// Tests for handling API approval.
/// </summary>
[ExcludeFromCodeCoverage]
public class ApiApprovalTests
{
    /// <summary>
    /// Tests to make sure the fusillade project is approved.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public Task FusilladeTests() => typeof(OfflineHttpMessageHandler).Assembly.CheckApproval(["Fusillade"]);
}
