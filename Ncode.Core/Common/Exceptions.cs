// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

namespace Ncode.Core.Common;

public class BreakException : Exception { }
public class ContinueException : Exception { }
public class ExitException : Exception { }

public class NcodeRuntimeException : Exception
{
    public int Line { get; }

    public NcodeRuntimeException(string message, int line) : base($"строка {line}: {message}")
    {
        Line = line;
    }
}
