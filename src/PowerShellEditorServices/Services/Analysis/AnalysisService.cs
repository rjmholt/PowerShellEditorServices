﻿//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using System.Threading;
using System.Collections.Concurrent;
using Microsoft.PowerShell.EditorServices.Services.TextDocument;
using Microsoft.PowerShell.EditorServices.Services.Analysis;
using Microsoft.PowerShell.EditorServices.Services.Configuration;
using System.Collections;
using System.Runtime.InteropServices;

namespace Microsoft.PowerShell.EditorServices.Services
{
    /// <summary>
    /// Provides a high-level service for performing semantic analysis
    /// of PowerShell scripts.
    /// </summary>
    internal class AnalysisService : IDisposable
    {
        /// <summary>
        /// Reliably generate an ID for a diagnostic record to track it.
        /// </summary>
        /// <param name="diagnostic">The diagnostic to generate an ID for.</param>
        /// <returns>A string unique to this diagnostic given where and what kind it is.</returns>
        internal static string GetUniqueIdFromDiagnostic(Diagnostic diagnostic)
        {
            Position start = diagnostic.Range.Start;
            Position end = diagnostic.Range.End;

            var sb = new StringBuilder(256)
            .Append(diagnostic.Source ?? "?")
            .Append("_")
            .Append(diagnostic.Code.IsString ? diagnostic.Code.String : diagnostic.Code.Long.ToString())
            .Append("_")
            .Append(diagnostic.Severity?.ToString() ?? "?")
            .Append("_")
            .Append(start.Line)
            .Append(":")
            .Append(start.Character)
            .Append("-")
            .Append(end.Line)
            .Append(":")
            .Append(end.Character);

            var id = sb.ToString();
            return id;
        }

        /// <summary>
        /// Defines the list of Script Analyzer rules to include by default if
        /// no settings file is specified.
        /// </summary>
        private static readonly string[] s_defaultRules = {
            "PSAvoidAssignmentToAutomaticVariable",
            "PSUseToExportFieldsInManifest",
            "PSMisleadingBacktick",
            "PSAvoidUsingCmdletAliases",
            "PSUseApprovedVerbs",
            "PSAvoidUsingPlainTextForPassword",
            "PSReservedCmdletChar",
            "PSReservedParams",
            "PSShouldProcess",
            "PSMissingModuleManifestField",
            "PSAvoidDefaultValueSwitchParameter",
            "PSUseDeclaredVarsMoreThanAssignments",
            "PSPossibleIncorrectComparisonWithNull",
            "PSAvoidDefaultValueForMandatoryParameter",
            "PSPossibleIncorrectUsageOfRedirectionOperator"
        };

        private readonly ILoggerFactory _loggerFactory;

        private readonly ILogger _logger;

        private readonly ILanguageServer _languageServer;

        private readonly ConfigurationService _configurationService;

        private readonly WorkspaceService _workplaceService;

        private readonly int _analysisDelayMillis;

        private readonly ConcurrentDictionary<string, CorrectionTableEntry> _mostRecentCorrectionsByFile;

        private bool _hasInstantiatedAnalysisEngine;

        private PssaCmdletAnalysisEngine _analysisEngine;

        private CancellationTokenSource _diagnosticsCancellationTokenSource;

        /// <summary>
        /// Construct a new AnalysisService.
        /// </summary>
        /// <param name="loggerFactory">Logger factory to create logger instances with.</param>
        /// <param name="languageServer">The LSP language server for notifications.</param>
        /// <param name="configurationService">The configuration service to query for configurations.</param>
        /// <param name="workspaceService">The workspace service for file handling within a workspace.</param>
        public AnalysisService(
            ILoggerFactory loggerFactory,
            ILanguageServer languageServer,
            ConfigurationService configurationService,
            WorkspaceService workspaceService)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<AnalysisService>();
            _languageServer = languageServer;
            _configurationService = configurationService;
            _workplaceService = workspaceService;
            _analysisDelayMillis = 750;
            _mostRecentCorrectionsByFile = new ConcurrentDictionary<string, CorrectionTableEntry>();
            _hasInstantiatedAnalysisEngine = false;
        }

        /// <summary>
        /// The analysis engine to use for running script analysis.
        /// </summary>
        private PssaCmdletAnalysisEngine AnalysisEngine
        {
            get
            {
                if (!_hasInstantiatedAnalysisEngine)
                {
                    _analysisEngine = InstantiateAnalysisEngine();
                    _hasInstantiatedAnalysisEngine = true;
                }

                return _analysisEngine;
            }
        }

        /// <summary>
        /// Sets up a script analysis run, eventually returning the result.
        /// </summary>
        /// <param name="filesToAnalyze">The files to run script analysis on.</param>
        /// <param name="cancellationToken">A cancellation token to cancel this call with.</param>
        /// <returns>A task that finishes when script diagnostics have been published.</returns>
        public void RunScriptDiagnostics(
            ScriptFile[] filesToAnalyze,
            CancellationToken cancellationToken)
        {
            if (AnalysisEngine == null)
            {
                return;
            }

            // Create a cancellation token source that will cancel if we do or if the caller does
            var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            CancellationTokenSource oldTaskCancellation;
            // If there's an existing task, we want to cancel it here
            if ((oldTaskCancellation = Interlocked.Exchange(ref _diagnosticsCancellationTokenSource, cancellationSource)) != null)
            {
                try
                {
                    oldTaskCancellation.Cancel();
                    oldTaskCancellation.Dispose();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Exception occurred while cancelling analysis task");
                }
            }

            if (filesToAnalyze.Length == 0)
            {
                return;
            }

            var analysisTask = Task.Run(() => DelayThenInvokeDiagnosticsAsync(filesToAnalyze, _diagnosticsCancellationTokenSource.Token), _diagnosticsCancellationTokenSource.Token);

            // Ensure that any next corrections request will wait for this diagnostics publication
            foreach (ScriptFile file in filesToAnalyze)
            {
                CorrectionTableEntry fileCorrectionsEntry = _mostRecentCorrectionsByFile.GetOrAdd(
                    file.DocumentUri,
                    CorrectionTableEntry.CreateForFile);

                fileCorrectionsEntry.DiagnosticPublish = analysisTask;
            }
        }

        /// <summary>
        /// Formats a PowerShell script with the given settings.
        /// </summary>
        /// <param name="scriptFileContents">The script to format.</param>
        /// <param name="formatSettings">The settings to use with the formatter.</param>
        /// <param name="formatRange">Optionally, the range that should be formatted.</param>
        /// <returns>The text of the formatted PowerShell script.</returns>
        public Task<string> FormatAsync(string scriptFileContents, Hashtable formatSettings, int[] formatRange = null)
        {
            if (AnalysisEngine == null)
            {
                return null;
            }

            return AnalysisEngine.FormatAsync(scriptFileContents, formatSettings, formatRange);
        }

        /// <summary>
        /// Get comment help text for a PowerShell function definition.
        /// </summary>
        /// <param name="functionText">The text of the function to get comment help for.</param>
        /// <param name="helpLocation">A string referring to which location comment help should be placed around the function.</param>
        /// <param name="forBlockComment">If true, block comment help will be supplied.</param>
        /// <returns></returns>
        public async Task<string> GetCommentHelpText(string functionText, string helpLocation, bool forBlockComment)
        {
            if (AnalysisEngine == null)
            {
                return null;
            }

            Hashtable commentHelpSettings = AnalysisService.GetCommentHelpRuleSettings(helpLocation, forBlockComment);

            ScriptFileMarker[] analysisResults = await AnalysisEngine.AnalyzeScriptAsync(functionText, commentHelpSettings).ConfigureAwait(false);

            if (analysisResults.Length == 0
                || analysisResults[0]?.Correction?.Edits == null
                || analysisResults[0].Correction.Edits.Count() == 0)
            {
                return null;
            }

            return analysisResults[0].Correction.Edits[0].Text;
        }

        /// <summary>
        /// Get the most recent corrections computed for a given script file.
        /// </summary>
        /// <param name="documentUri">The URI string of the file to get code actions for.</param>
        /// <returns>A threadsafe readonly dictionary of the code actions of the particular file.</returns>
        public async Task<IReadOnlyDictionary<string, MarkerCorrection>> GetMostRecentCodeActionsForFileAsync(string documentUri)
        {
            // On Windows, VSCode still gives us file URIs like "file:///c%3a/...", so we need to escape them
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var uri = new Uri(documentUri);
                if (uri.Scheme == "file")
                {
                    documentUri = WorkspaceService.UnescapeDriveColon(uri).AbsoluteUri;
                }
            }


            if (!_mostRecentCorrectionsByFile.TryGetValue(documentUri, out CorrectionTableEntry corrections))
            {
                return null;
            }

            // Wait for diagnostics to be published for this file
            await corrections.DiagnosticPublish.ConfigureAwait(false);

            return corrections.Corrections;
        }

        /// <summary>
        /// Clear all diagnostic markers for a given file.
        /// </summary>
        /// <param name="file">The file to clear markers in.</param>
        /// <returns>A task that ends when all markers in the file have been cleared.</returns>
        public void ClearMarkers(ScriptFile file)
        {
            PublishScriptDiagnostics(file, Array.Empty<ScriptFileMarker>());
        }

        /// <summary>
        /// Event subscription method to be run when PSES configuration has been updated.
        /// </summary>
        /// <param name="sender">The sender of the configuration update event.</param>
        /// <param name="settings">The new language server settings.</param>
        public void OnConfigurationUpdated(object sender, LanguageServerSettings settings)
        {
            ClearOpenFileMarkers();
            _analysisEngine = InstantiateAnalysisEngine();
        }

        private PssaCmdletAnalysisEngine InstantiateAnalysisEngine()
        {
            if (!(_configurationService.CurrentSettings.ScriptAnalysis.Enable ?? false))
            {
                return null;
            }

            var pssaCmdletEngineBuilder = new PssaCmdletAnalysisEngine.Builder(_loggerFactory);

            // If there's a settings file use that
            if (TryFindSettingsFile(out string settingsFilePath))
            {
                _logger.LogInformation($"Configuring PSScriptAnalyzer with rules at '{settingsFilePath}'");
                pssaCmdletEngineBuilder.WithSettingsFile(settingsFilePath);
            }
            else
            {
                _logger.LogInformation("PSScriptAnalyzer settings file not found. Falling back to default rules");
                pssaCmdletEngineBuilder.WithIncludedRules(s_defaultRules);
            }

            return pssaCmdletEngineBuilder.Build();
        }

        private bool TryFindSettingsFile(out string settingsFilePath)
        {
            string configuredPath = _configurationService.CurrentSettings.ScriptAnalysis.SettingsPath;

            if (!string.IsNullOrEmpty(configuredPath))
            {
                settingsFilePath = _workplaceService.ResolveWorkspacePath(configuredPath);

                if (settingsFilePath == null)
                {
                    _logger.LogError($"Unable to find PSSA settings file at '{configuredPath}'. Loading default rules.");
                }

                return settingsFilePath != null;
            }

            // TODO: Could search for a default here

            settingsFilePath = null;
            return false;
        }

        private void ClearOpenFileMarkers()
        {
            foreach (ScriptFile file in _workplaceService.GetOpenedFiles())
            {
                ClearMarkers(file);
            }
        }

        private async Task DelayThenInvokeDiagnosticsAsync(ScriptFile[] filesToAnalyze, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(_analysisDelayMillis, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            // If we've made it past the delay period then we don't care
            // about the cancellation token anymore.  This could happen
            // when the user stops typing for long enough that the delay
            // period ends but then starts typing while analysis is going
            // on.  It makes sense to send back the results from the first
            // delay period while the second one is ticking away.

            foreach (ScriptFile scriptFile in filesToAnalyze)
            {
                ScriptFileMarker[] semanticMarkers = await AnalysisEngine.AnalyzeScriptAsync(scriptFile.Contents).ConfigureAwait(false);

                scriptFile.DiagnosticMarkers.AddRange(semanticMarkers);

                PublishScriptDiagnostics(scriptFile);
            }
        }

        private void PublishScriptDiagnostics(ScriptFile scriptFile) => PublishScriptDiagnostics(scriptFile, scriptFile.DiagnosticMarkers);

        private void PublishScriptDiagnostics(ScriptFile scriptFile, IReadOnlyList<ScriptFileMarker> markers)
        {
            var diagnostics = new Diagnostic[scriptFile.DiagnosticMarkers.Count];

            CorrectionTableEntry fileCorrections = _mostRecentCorrectionsByFile.GetOrAdd(
                scriptFile.DocumentUri,
                CorrectionTableEntry.CreateForFile);

            fileCorrections.Corrections.Clear();

            for (int i = 0; i < markers.Count; i++)
            {
                ScriptFileMarker marker = markers[i];

                Diagnostic diagnostic = GetDiagnosticFromMarker(marker);

                if (marker.Correction != null)
                {
                    string diagnosticId = GetUniqueIdFromDiagnostic(diagnostic);
                    fileCorrections.Corrections[diagnosticId] = marker.Correction;
                }

                diagnostics[i] = diagnostic;
            }

            _languageServer.Document.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = new Uri(scriptFile.DocumentUri),
                Diagnostics = new Container<Diagnostic>(diagnostics)
            });
        }

        private static ConcurrentDictionary<string, MarkerCorrection> CreateFileCorrectionsEntry(string fileUri)
        {
            return new ConcurrentDictionary<string, MarkerCorrection>();
        }

        private static Diagnostic GetDiagnosticFromMarker(ScriptFileMarker scriptFileMarker)
        {
            return new Diagnostic
            {
                Severity = MapDiagnosticSeverity(scriptFileMarker.Level),
                Message = scriptFileMarker.Message,
                Code = scriptFileMarker.RuleName,
                Source = scriptFileMarker.Source,
                Range = new Range
                {
                    Start = new Position
                    {
                        Line = scriptFileMarker.ScriptRegion.StartLineNumber - 1,
                        Character = scriptFileMarker.ScriptRegion.StartColumnNumber - 1
                    },
                    End = new Position
                    {
                        Line = scriptFileMarker.ScriptRegion.EndLineNumber - 1,
                        Character = scriptFileMarker.ScriptRegion.EndColumnNumber - 1
                    }
                }
            };
        }

        private static DiagnosticSeverity MapDiagnosticSeverity(ScriptFileMarkerLevel markerLevel)
        {
            switch (markerLevel)
            {
                case ScriptFileMarkerLevel.Error:       return DiagnosticSeverity.Error;
                case ScriptFileMarkerLevel.Warning:     return DiagnosticSeverity.Warning;
                case ScriptFileMarkerLevel.Information: return DiagnosticSeverity.Information;
                default:                                return DiagnosticSeverity.Error;
            };
        }

        private static Hashtable GetCommentHelpRuleSettings(string helpLocation, bool forBlockComment)
        {
            return new Hashtable {
                { "Rules", new Hashtable {
                    { "PSProvideCommentHelp", new Hashtable {
                        { "Enable", true },
                        { "ExportedOnly", false },
                        { "BlockComment", forBlockComment },
                        { "VSCodeSnippetCorrection", true },
                        { "Placement", helpLocation }}
                    }}
                }};
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _analysisEngine?.Dispose();
                    _diagnosticsCancellationTokenSource?.Dispose();
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion

        private class CorrectionTableEntry
        {
            public static CorrectionTableEntry CreateForFile(string fileUri)
            {
                return new CorrectionTableEntry();
            }

            public CorrectionTableEntry()
            {
                Corrections = new ConcurrentDictionary<string, MarkerCorrection>();
                DiagnosticPublish = Task.CompletedTask;
            }

            public ConcurrentDictionary<string, MarkerCorrection> Corrections { get; }

            public Task DiagnosticPublish { get; set; }
        }
    }
}
