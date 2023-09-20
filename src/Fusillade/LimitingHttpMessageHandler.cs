// Copyright (c) 2023 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Net.Http;

namespace Fusillade;

/// <summary>
/// Limiting HTTP schedulers only allow a certain number of bytes to be
/// read before cancelling all future requests. This is designed for
/// reading data that may or may not be used by the user later, in order
/// to improve response times should the user later request the data.
/// </summary>
public abstract class LimitingHttpMessageHandler : DelegatingHandler
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LimitingHttpMessageHandler"/> class.
    /// </summary>
    /// <param name="innerHandler">A inner handler we will call to get the data.</param>
    protected LimitingHttpMessageHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LimitingHttpMessageHandler"/> class.
    /// </summary>
    protected LimitingHttpMessageHandler()
    {
    }

    /// <summary>
    /// Resets the total limit of bytes to read. This is usually called
    /// when the app resumes from suspend, to indicate that we should
    /// fetch another set of data.
    /// </summary>
    /// <param name="maxBytesToRead">The maximum number of bytes to read.</param>
    public abstract void ResetLimit(long? maxBytesToRead = null);
}
