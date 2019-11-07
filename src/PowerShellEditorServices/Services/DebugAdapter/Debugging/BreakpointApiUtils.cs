//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Management.Automation;
using System.Reflection;

namespace Microsoft.PowerShell.EditorServices.Services.DebugAdapter

{
    internal static class BreakpointApiUtils
    {
        private static readonly bool s_supportsNewBreakpointApis;

        private static readonly Lazy<Func<Debugger, string, int, int, ScriptBlock, LineBreakpoint>> s_setLineBreakpointLazy;

        private static readonly Lazy<Func<Debugger, string, ScriptBlock, string, CommandBreakpoint>> s_setCommandBreakpointLazy;

        private static readonly Lazy<Func<string, int, int, ScriptBlock, LineBreakpoint>> s_newLineBreakpointLazy;

        private static readonly Lazy<Func<string, WildcardPattern, string, ScriptBlock, CommandBreakpoint>> s_newCommandBreakpointLazy;

        static BreakpointApiUtils()
        {
            s_supportsNewBreakpointApis = typeof(Debugger).GetMethod("SetLineBreakpoint", BindingFlags.Public | BindingFlags.Instance) != null;

            if (s_supportsNewBreakpointApis)
            {

                s_setLineBreakpointLazy = new Lazy<Func<Debugger, string, int, int, ScriptBlock, LineBreakpoint>>(() =>
                {
                    MethodInfo setLineBreakpointMethod = typeof(Debugger).GetMethod("SetLineBreakpoint", BindingFlags.Public | BindingFlags.Instance);

                    return (Func<Debugger, string, int, int, ScriptBlock, LineBreakpoint>)Delegate.CreateDelegate(
                        typeof(Func<Debugger, string, int, int, ScriptBlock, LineBreakpoint>),
                        firstArgument: null,
                        setLineBreakpointMethod);
                });

                s_setCommandBreakpointLazy = new Lazy<Func<Debugger, string, ScriptBlock, string, CommandBreakpoint>>(() =>
                {
                    MethodInfo setCommandBreakpointMethod = typeof(Debugger).GetMethod("SetCommandBreakpoint", BindingFlags.Public | BindingFlags.Instance);

                    return (Func<Debugger, string, ScriptBlock, string, CommandBreakpoint>)Delegate.CreateDelegate(
                        typeof(Func<Debugger, string, ScriptBlock, string, CommandBreakpoint>),
                        firstArgument: null,
                        setCommandBreakpointMethod);
                });

                return;
            }

            s_newLineBreakpointLazy = new Lazy<Func<string, int, int, ScriptBlock, LineBreakpoint>>(() =>
            {
                ConstructorInfo lineBreakpointCtor = typeof(LineBreakpoint).GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null,
                    new Type[] { typeof(string), typeof(int), typeof(int), typeof(ScriptBlock) },
                    modifiers: null);

                var ctorParams = new List<ParameterExpression>()
                {
                    Expression.Parameter(typeof(string), "script"),
                    Expression.Parameter(typeof(int), "line"),
                    Expression.Parameter(typeof(int), "column"),
                    Expression.Parameter(typeof(ScriptBlock), "action"),
                };

                return Expression.Lambda<Func<string, int, int, ScriptBlock, LineBreakpoint>>(Expression.New(lineBreakpointCtor, ctorParams), ctorParams)
                    .Compile();
            });

            s_newCommandBreakpointLazy = new Lazy<Func<string, WildcardPattern, string, ScriptBlock, CommandBreakpoint>>(() =>
            {
                ConstructorInfo commandBreakpointCtor = typeof(CommandBreakpoint).GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null,
                    new Type[] { typeof(string), typeof(WildcardPattern), typeof(string), typeof(ScriptBlock) },
                    modifiers: null);

                var ctorParams = new List<ParameterExpression>()
                {
                    Expression.Parameter(typeof(string), "path"),
                    Expression.Parameter(typeof(WildcardPattern), "pattern"),
                    Expression.Parameter(typeof(string), "command"),
                    Expression.Parameter(typeof(ScriptBlock), "action"),
                };

                return Expression.Lambda<Func<string, WildcardPattern, string, ScriptBlock, CommandBreakpoint>>(Expression.New(commandBreakpointCtor, ctorParams), ctorParams)
                    .Compile();

            });
        }

        private static Func<Debugger, string, int, int, ScriptBlock, LineBreakpoint> SetLineBreakpoint => s_setLineBreakpointLazy.Value;

        private static Func<Debugger, string, ScriptBlock, string, CommandBreakpoint> SetCommandBreakpoint => s_setCommandBreakpointLazy.Value;

        private static Func<string, int, int, ScriptBlock, LineBreakpoint> CreateLineBreakpoint => s_newLineBreakpointLazy.Value;

        private static Func<string, WildcardPattern, string, ScriptBlock, CommandBreakpoint> CreateCommandBreakpoint => s_newCommandBreakpointLazy.Value;

        public static IEnumerable<Breakpoint> SetBreakpoints(Debugger debugger, IEnumerable<BreakpointDetailsBase> breakpoints)
        {
            var psBreakpoints = new List<Breakpoint>(breakpoints.Count());

            foreach (BreakpointDetailsBase breakpoint in breakpoints)
            {
                Breakpoint psBreakpoint;
                switch (breakpoint)
                {
                    case BreakpointDetails lineBreakpoint:
                        psBreakpoint = s_supportsNewBreakpointApis
                            ? SetLineBreakpoint(debugger, lineBreakpoint.Source, lineBreakpoint.LineNumber, lineBreakpoint.ColumnNumber ?? 0, null)
                            : CreateLineBreakpoint(lineBreakpoint.Source, lineBreakpoint.LineNumber, lineBreakpoint.ColumnNumber ?? 0, null);
                        break;

                    case CommandBreakpointDetails commandBreakpoint:
                        psBreakpoint = s_supportsNewBreakpointApis
                            ? SetCommandBreakpoint(debugger, commandBreakpoint.Name, null, null)
                            : CreateCommandBreakpoint(null, WildcardPattern.Get(commandBreakpoint.Name, WildcardOptions.IgnoreCase), commandBreakpoint.Name, null);
                        break;

                    default:
                        throw new NotImplementedException("Other breakpoints not supported yet");
                }

                psBreakpoints.Add(psBreakpoint);
            }

            if (!s_supportsNewBreakpointApis)
            {
                debugger.SetBreakpoints(psBreakpoints);
            }

            return psBreakpoints;
        }
    }
}
