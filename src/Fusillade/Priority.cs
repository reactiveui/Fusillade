// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive;
#else
namespace Fusillade;
#endif

/// <summary>
/// This enumeration defines the default base priorities associated with the
/// different NetCache instances.
/// </summary>
public enum Priority
{
    /// <summary>This is a explicit task.</summary>
    Explicit = 0,

    /// <summary>A speculative priority where we aren't sure.</summary>
    Speculative = 10,

    /// <summary>This is background based task.</summary>
    Background = 20,

    /// <summary>This is a instance which is initiated by the user.</summary>
    UserInitiated = 100,
}
