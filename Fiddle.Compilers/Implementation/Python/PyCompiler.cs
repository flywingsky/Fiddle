﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;

namespace Fiddle.Compilers.Implementation.Python {
    public class PyCompiler : ICompiler {
        public PyCompiler(string code) : this(code, new CompilerProperties(), new ExecutionProperties()) { }

        public PyCompiler(string code, ICompilerProperties cProps, IExecutionProperties eProps) {
            SourceCode = code;
            CompilerProperties = cProps;
            ExecuteProperties = eProps;
        }

        public MemoryStream Stream { get; set; }
        public StringWriter Writer { get; set; }
        private ScriptScope Scope { get; set; }
        private CompiledCode Compilation { get; set; }
        private ScriptSource Source { get; set; }
        public IExecutionProperties ExecuteProperties { get; }
        public ICompilerProperties CompilerProperties { get; }
        public string SourceCode { get; set; }
        public ICompileResult CompileResult { get; private set; }
        public IExecuteResult ExecuteResult { get; private set; }
        public Language Language { get; } = Language.Python;


        public async Task<ICompileResult> Compile() {
            TaskCompletionSource<ICompileResult> tcs = new TaskCompletionSource<ICompileResult>();

            new Thread(() => {
                PyErrorListener listener = new PyErrorListener();
                Init();

                //Start the stopwatch
                Stopwatch sw = Stopwatch.StartNew();
                //Spawn a new thread with the compile process
                Thread compileThread = new Thread(() => {
                    ScriptEngine engine = IronPython.Hosting.Python.CreateEngine();
                    engine.Runtime.IO.SetOutput(Stream, Writer);
                    Scope = engine.CreateScope();
                    Source = engine.CreateScriptSourceFromString(SourceCode);
                    Compilation = Source.Compile(listener);
                });
                compileThread.Start();
                //Join the thread into main thread with specified timeout
                bool graceful = compileThread.Join((int) CompilerProperties.Timeout);
                sw.Stop();

                if (!graceful)
                    listener.Diagnostics.Add(new PyDiagnostic("Compilation timed out!", 1, 1, 1, 1, Severity.Error));

                tcs.SetResult(new PyCompileResult(sw.ElapsedMilliseconds, SourceCode, listener.Diagnostics));
            }).Start();

            ICompileResult result = await tcs.Task;
            CompileResult = result;
            return result;
        }

        public async Task<IExecuteResult> Execute() {
            //recompile if source code changed, not yet compiled or Console has already text printed
            if (Compilation == null || SourceCode != Source.GetCode() || Writer.ToString().Length > 0)
                await Compile();
            if (Compilation == null || !CompileResult.Success) //if still not successful after compiling
                return new PyExecuteResult(-1, null, null, CompileResult,
                    new Exception("Compilation was not successful!"));

            ExecuteThreaded<object>.ThreadRunResult result = await ExecuteThreaded<object>.Execute(() =>
                    Compilation.Execute(Scope),
                (int) ExecuteProperties.Timeout);

            if (result.ReturnValue?.GetType() == typeof(Exception)) {
                IExecuteResult execResultInner = new PyExecuteResult(
                    result.ElapsedMilliseconds,
                    null, null,
                    CompileResult,
                    result.ReturnValue as Exception);
                ExecuteResult = execResultInner;
                return execResultInner;
            }

            IExecuteResult execResult = new PyExecuteResult(
                result.ElapsedMilliseconds,
                Writer.ToString(),
                result.ReturnValue,
                CompileResult,
                result.Exception);
            ExecuteResult = execResult;
            return execResult;
        }

        public void Dispose() {
            Stream?.Dispose();
            Writer?.Dispose();
        }

        private void Init() {
            Stream?.Dispose();
            Stream = new MemoryStream();
            Writer?.Dispose();
            Writer = new StringWriter();
        }


        public class PyErrorListener : ErrorListener {
            public PyErrorListener() {
                Diagnostics = new List<IDiagnostic>();
            }

            public IList<IDiagnostic> Diagnostics { get; set; }

            public override void ErrorReported(ScriptSource source, string message, SourceSpan span, int errorCode,
                Microsoft.Scripting.Severity severity) {
                //-1 because it's 1 based
                Diagnostics.Add(new PyDiagnostic(
                    message,
                    span.Start.Line,
                    span.End.Line,
                    span.Start.Column,
                    span.Start.Column,
                    Host.ToSeverity(severity)));
            }
        }
    }
}