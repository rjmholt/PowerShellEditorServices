﻿// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "PSES is not localized", Scope = "module")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by created object", Scope = "member", Target = "~M:Microsoft.PowerShell.EditorServices.Hosting.EditorServicesServerFactory.Create(System.String,System.Int32,System.IObservable{System.})~Microsoft.PowerShell.EditorServices.Hosting.EditorServicesServerFactory")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1810:Initialize reference type static fields inline", Justification = "cctor required for version-specific behavior", Scope = "member", Target = "~M:Microsoft.PowerShell.EditorServices.Services.PowerShellContextService.#cctor")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "PowerShellContext must catch all exceptions for robustness, logging them instead", Scope = "member", Target = "~M:Microsoft.PowerShell.EditorServices.Services.PowerShellContextService.ExecuteCommandAsync``1(System.Management.Automation.PSCommand,System.Text.StringBuilder,Microsoft.PowerShell.EditorServices.Services.PowerShellContext.ExecutionOptions)~System.Threading.Tasks.Task{System.Collections.Generic.IEnumerable{``0}}")]
