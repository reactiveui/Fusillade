// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
#if !NETCOREAPP3_0_OR_GREATER && !NETSTANDARD2_1_OR_GREATER

// Polyfill implementation adapted from Simon Cropp's Polyfill library
// https://github.com/SimonCropp/Polyfill
namespace System.Diagnostics.CodeAnalysis;

/// <summary>Specifies that an output is not <see langword="null"/> even if the corresponding type allows it.</summary>
[ExcludeFromCodeCoverage]
[DebuggerNonUserCode]
[AttributeUsage(
    validOn: AttributeTargets.Field |
             AttributeTargets.Parameter |
             AttributeTargets.Property |
             AttributeTargets.ReturnValue)]
internal sealed class NotNullAttribute : Attribute;
#endif
