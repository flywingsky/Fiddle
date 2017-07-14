﻿using System;

namespace Fiddle.Compilers.Implementation.CSharp
{
    public class CSharpDiagnostic : IDiagnostic
    {
        public string Message { get; }
        public int Line { get; }
        public int Char { get; }

        public CSharpDiagnostic(string message, int ln, int ch)
        {
            Message = message;
            Line = ln;
            Char = ch;
        }

        public Exception ToException()
        {
            return new Exception($"Ln{Line} Ch{Char}: {Message}");
        }
    }
}
