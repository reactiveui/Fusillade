// Copyright (c) 2016 - 2026 ReactiveUI and Contributors. All rights reserved.
// Licensed to ReactiveUI and Contributors under one or more agreements.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if !NET5_0_OR_GREATER

// Polyfill implementation adapted from Simon Cropp's Polyfill library
// https://github.com/SimonCropp/Polyfill
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.CompilerServices;

/// <summary>
/// Indicates that a parameter captures the expression passed for another parameter as a string.
/// </summary>
[ExcludeFromCodeCoverage]
[DebuggerNonUserCode]
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class CallerArgumentExpressionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CallerArgumentExpressionAttribute"/> class.
    /// </summary>
    /// <param name="parameterName">The name of the parameter whose expression should be captured as a string.</param>
    public CallerArgumentExpressionAttribute(string parameterName) =>
        ParameterName = parameterName;

    /// <summary>
    /// Gets the name of the parameter whose expression should be captured as a string.
    /// </summary>
    public string ParameterName { get; }
}

#else
using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo(typeof(CallerArgumentExpressionAttribute))]
#endif
