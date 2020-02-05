﻿//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Services.TextDocument;
using System;
using System.Collections;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices.Services.Analysis
{
    internal class NullAnalysisEngine : IAnalysisEngine
    {
        public bool IsEnabled => false;

        public Task<string> FormatAsync(string scriptDefinition, Hashtable settings, int[] rangeList)
        {
            throw CreateInvocationException();
        }

        public Task<ScriptFileMarker[]> GetSemanticMarkersAsync(string scriptFileContent, Hashtable settings)
        {
            throw CreateInvocationException();
        }

        public Task<ScriptFileMarker[]> GetSemanticMarkersAsync(string scriptFileContent, string settingsFilePath)
        {
            throw CreateInvocationException();
        }

        public Task<ScriptFileMarker[]> GetSemanticMarkersAsync(string scriptFileContent, string[] rules)
        {
            throw CreateInvocationException();
        }

        private Exception CreateInvocationException()
        {
            return new InvalidOperationException($"{nameof(NullAnalysisEngine)} implements no functionality and its methods should not be called");
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }

}
