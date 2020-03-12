//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.PowerShell.EditorServices.Logging;
using Microsoft.PowerShell.EditorServices.Services;
using Microsoft.PowerShell.EditorServices.Utility;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace Microsoft.PowerShell.EditorServices.Handlers
{
    internal class DocumentFormattingHandler : IDocumentFormattingHandler
    {
        private readonly ILogger _logger;
        private readonly AnalysisService _analysisService;
        private readonly ConfigurationService _configurationService;
        private readonly WorkspaceService _workspaceService;
        private DocumentFormattingCapability _capability;

        public DocumentFormattingHandler(ILoggerFactory factory, AnalysisService analysisService, ConfigurationService configurationService, WorkspaceService workspaceService)
        {
            _logger = factory.CreateLogger<DocumentFormattingHandler>();
            _analysisService = analysisService;
            _configurationService = configurationService;
            _workspaceService = workspaceService;
        }

        public TextDocumentRegistrationOptions GetRegistrationOptions()
        {
            return new TextDocumentRegistrationOptions
            {
                DocumentSelector = LspUtils.PowerShellDocumentSelector
            };
        }

        public async Task<TextEditContainer> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
        {
            try
            {
                var scriptFile = _workspaceService.GetFile(request.TextDocument.Uri);
                var pssaSettings = _configurationService.CurrentSettings.CodeFormatting.GetPSSASettingsHashtable(
                    (int)request.Options.TabSize,
                    request.Options.InsertSpaces);


                // TODO raise an error event in case format returns null
                string formattedScript;
                Range editRange;
                var extent = scriptFile.ScriptAst.Extent;

                // todo create an extension for converting range to script extent
                editRange = new Range
                {
                    Start = new Position
                    {
                        Line = extent.StartLineNumber - 1,
                        Character = extent.StartColumnNumber - 1
                    },
                    End = new Position
                    {
                        Line = extent.EndLineNumber - 1,
                        Character = extent.EndColumnNumber - 1
                    }
                };

                formattedScript = await _analysisService.FormatAsync(
                    scriptFile.Contents,
                    pssaSettings,
                    null).ConfigureAwait(false);
                formattedScript = formattedScript ?? scriptFile.Contents;

                return new TextEditContainer(new TextEdit
                {
                    NewText = formattedScript,
                    Range = editRange
                });
            }
            catch (Exception e)
            {
                string optionsStr = $"{{ tabSize: {request.Options.TabSize}; insertSpaces: {request.Options.InsertSpaces} }}";
                _logger.LogException($"Exception occurred formatting document at '{request.TextDocument.Uri}' with options {optionsStr}", e);
            }
        }

        public void SetCapability(DocumentFormattingCapability capability)
        {
            _capability = capability;
        }
    }

    internal class DocumentRangeFormattingHandler : IDocumentRangeFormattingHandler
    {
        private readonly ILogger _logger;
        private readonly AnalysisService _analysisService;
        private readonly ConfigurationService _configurationService;
        private readonly WorkspaceService _workspaceService;
        private DocumentRangeFormattingCapability _capability;

        public DocumentRangeFormattingHandler(ILoggerFactory factory, AnalysisService analysisService, ConfigurationService configurationService, WorkspaceService workspaceService)
        {
            _logger = factory.CreateLogger<DocumentRangeFormattingHandler>();
            _analysisService = analysisService;
            _configurationService = configurationService;
            _workspaceService = workspaceService;
        }

        public TextDocumentRegistrationOptions GetRegistrationOptions()
        {
            return new TextDocumentRegistrationOptions
            {
                DocumentSelector = LspUtils.PowerShellDocumentSelector
            };
        }

        public async Task<TextEditContainer> Handle(DocumentRangeFormattingParams request, CancellationToken cancellationToken)
        {
            var scriptFile = _workspaceService.GetFile(request.TextDocument.Uri);
            var pssaSettings = _configurationService.CurrentSettings.CodeFormatting.GetPSSASettingsHashtable(
                (int)request.Options.TabSize,
                request.Options.InsertSpaces);

            // TODO raise an error event in case format returns null;
            string formattedScript;
            Range editRange;
            var extent = scriptFile.ScriptAst.Extent;

            // TODO create an extension for converting range to script extent
            editRange = new Range
            {
                Start = new Position
                {
                    Line = extent.StartLineNumber - 1,
                    Character = extent.StartColumnNumber - 1
                },
                End = new Position
                {
                    Line = extent.EndLineNumber - 1,
                    Character = extent.EndColumnNumber - 1
                }
            };

            Range range = request.Range;
            var rangeList = range == null ? null : new int[] {
                (int)range.Start.Line + 1,
                (int)range.Start.Character + 1,
                (int)range.End.Line + 1,
                (int)range.End.Character + 1};

            formattedScript = await _analysisService.FormatAsync(
                scriptFile.Contents,
                pssaSettings,
                rangeList).ConfigureAwait(false);
            formattedScript = formattedScript ?? scriptFile.Contents;

            return new TextEditContainer(new TextEdit
            {
                NewText = formattedScript,
                Range = editRange
            });
        }

        public void SetCapability(DocumentRangeFormattingCapability capability)
        {
            _capability = capability;
        }
    }
}
