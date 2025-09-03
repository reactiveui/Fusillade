﻿// Copyright (c) 2025 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;

namespace Fusillade;

internal static class ConcatenateMixin
{
    public static string ConcatenateAll<T>(this IEnumerable<T> enumerables, Func<T, string> selector, char separator = '|') =>
        enumerables.Aggregate(new StringBuilder(), (acc, x) =>
        {
            acc.Append(selector(x)).Append(separator);
            return acc;
        }).ToString();
}
