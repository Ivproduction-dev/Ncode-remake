// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

namespace Ncode.Editor;

public enum DiagnosticSeverity { Warning, Error }

public record CodeDiagnostic(string File, int Line, string Message, DiagnosticSeverity Severity);
