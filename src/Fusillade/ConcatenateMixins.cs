// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Fusillade;

/// <summary>Extension helpers for joining sequences into a single delimited string.</summary>
[SuppressMessage("Roslynator", "RCS1263:Invalid reference in documentation comment", Justification = "Roslynator does not yet understand C# extension block receiver documentation.")]
internal static class ConcatenateMixins
{
    /// <summary>Concatenation extension methods for sequences.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="enumerables">The source sequence.</param>
    extension<T>(IEnumerable<T> enumerables)
    {
        /// <summary>Concatenates the projected values of a sequence, separated by <paramref name="separator"/>.</summary>
        /// <param name="selector">Projects each element to a string.</param>
        /// <param name="separator">The separator appended after each value.</param>
        /// <returns>The concatenated string.</returns>
        public string ConcatenateAll(Func<T, string> selector, char separator = '|') =>
            enumerables.Aggregate(new StringBuilder(), (acc, x) =>
            {
                acc.Append(selector(x)).Append(separator);
                return acc;
            }).ToString();
    }
}
