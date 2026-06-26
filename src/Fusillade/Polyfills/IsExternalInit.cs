// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if !NET

// Polyfill implementation adapted from Simon Cropp's Polyfill library
// https://github.com/SimonCropp/Polyfill

namespace System.Runtime.CompilerServices;

/// <summary>Reserved to be used by the compiler for tracking metadata. This class should not be used by developers in source code.</summary>
[ExcludeFromCodeCoverage]
[DebuggerNonUserCode]
internal sealed class IsExternalInit
{
    /// <summary>Initializes a new instance of the <see cref="IsExternalInit"/> class.</summary>
    private IsExternalInit()
    {
    }
}

#else
using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo(typeof(IsExternalInit))]
#endif
