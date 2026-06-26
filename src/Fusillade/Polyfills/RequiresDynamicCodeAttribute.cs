// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if !NET7_0_OR_GREATER

// Polyfill implementation adapted from Simon Cropp's Polyfill library
// https://github.com/SimonCropp/Polyfill
#nullable enable

using Targets = System.AttributeTargets;

namespace System.Diagnostics.CodeAnalysis;

/// <summary>
/// Indicates that the use of dynamic code is required by the target member. Dynamic code is code that is generated at runtime, such as through reflection or expression trees.
/// </summary>
[ExcludeFromCodeCoverage]
[DebuggerNonUserCode]
[AttributeUsage(
    validOn: Targets.Method |
             Targets.Constructor |
             Targets.Class,
    Inherited = false)]
internal sealed class RequiresDynamicCodeAttribute :
    Attribute
{
    /// <summary>Initializes a new instance of the <see cref="RequiresDynamicCodeAttribute"/> class with the specified message.</summary>
    /// <param name="message">A message that contains information about the usage of dynamic code.</param>
    public RequiresDynamicCodeAttribute(string message) =>
        Message = message;

    /// <summary>Gets or sets a value indicating whether the annotation should not apply to static members.</summary>
    public bool ExcludeStatics { get; set; }

    /// <summary>Gets a message that contains information about the usage of dynamic code.</summary>
    public string Message { get; }

    /// <summary>
    /// Gets or sets an optional URL that contains more information about the method,
    /// why it requires dynamic code, and what options a consumer has to deal with it.
    /// </summary>
    public string? Url { get; set; }
}
#else

[assembly: TypeForwardedTo(typeof(RequiresDynamicCodeAttribute))]
#endif
