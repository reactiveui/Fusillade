// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace Fusillade
{
    /// <summary>
    /// This enumeration defines the default base priorities associated with the
    /// different NetCache instances.
    /// </summary>
    public enum Priority
    {
        /// <summary>
        /// A speculative priority where we aren't sure.
        /// </summary>
        Speculative = 10,

        /// <summary>
        /// This is a instance which is initiated by the user.
        /// </summary>
        UserInitiated = 100,

        /// <summary>
        /// This is background based task.
        /// </summary>
        Background = 20,

        /// <summary>
        /// This is a explicit task.
        /// </summary>
        Explicit = 0,
    }
}
