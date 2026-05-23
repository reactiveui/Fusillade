// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fusillade;

/// <summary>
/// Extension helpers for joining sequences into a single delimited string.
/// </summary>
internal static class ConcatenateMixin
{
    /// <summary>
    /// Concatenates the projected values of a sequence, separated by <paramref name="separator"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="enumerables">The source sequence.</param>
    /// <param name="selector">Projects each element to a string.</param>
    /// <param name="separator">The separator appended after each value.</param>
    /// <returns>The concatenated string.</returns>
    public static string ConcatenateAll<T>(this IEnumerable<T> enumerables, Func<T, string> selector, char separator = '|') =>
        enumerables.Aggregate(new StringBuilder(), (acc, x) =>
        {
            acc.Append(selector(x)).Append(separator);
            return acc;
        }).ToString();
}
