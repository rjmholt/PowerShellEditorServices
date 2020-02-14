﻿//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Services.PowerShellContext;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices.Extensions.Services
{
    /// <summary>
    /// Object specifying a UI prompt option to display to the user.
    /// </summary>
    public sealed class PromptChoiceDetails
    {
        /// <summary>
        /// Construct a prompt choice object for display in a prompt to the user.
        /// </summary>
        /// <param name="label">The label to identify this prompt choice. May not contain commas (',').</param>
        /// <param name="helpMessage">The message to display to users.</param>
        public PromptChoiceDetails(string label, string helpMessage)
        {
            if (label == null)
            {
                throw new ArgumentNullException(nameof(label));
            }

            if (label.Contains(","))
            {
                throw new ArgumentException($"Labels may not contain ','. Label: '{label}'", nameof(label));
            }

            Label = label;
            HelpMessage = helpMessage;
        }

        /// <summary>
        /// The label to identify this prompt message.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// The message to display to users in the UI for this prompt choice.
        /// </summary>
        public string HelpMessage { get; }
    }

    /// <summary>
    /// A service to manipulate the editor user interface.
    /// </summary>
    public interface IEditorUIService
    {
        /// <summary>
        /// Prompt a single selection from a set of choices.
        /// </summary>
        /// <param name="message">The message to display for the prompt.</param>
        /// <param name="choices">The choices to give the user.</param>
        /// <returns>The label of the selected choice, or null if the prompt was canceled.</returns>
        Task<string> PromptSelection(string message, IReadOnlyList<PromptChoiceDetails> choices);

        /// <summary>
        /// Prompt a single selection from a set of choices.
        /// </summary>
        /// <param name="message">The message to display for the prompt.</param>
        /// <param name="choices">The choices to give the user.</param>
        /// <param name="defaultChoiceIndex">The index in the choice list of the default choice.</param>
        /// <returns>The label of the selected choice, or null if the prompt was canceled.</returns>
        Task<string> PromptSelection(string message, IReadOnlyList<PromptChoiceDetails> choices, int defaultChoiceIndex);

        /// <summary>
        /// Prompt a set of selections from a list of choices.
        /// </summary>
        /// <param name="message">The message to display for the prompt.</param>
        /// <param name="choices">The choices to give the user.</param>
        /// <returns>A list of the labels of selected choices, or null if the prompt was canceled.</returns>
        Task<IReadOnlyList<string>> PromptMultipleSelection(string message, IReadOnlyList<PromptChoiceDetails> choices);

        /// <summary>
        /// Prompt a set of selections from a list of choices.
        /// </summary>
        /// <param name="message">The message to display for the prompt.</param>
        /// <param name="choices">The choices to give the user.</param>
        /// <param name="defaultChoiceIndexes">A list of the indexes of choices to mark as default.</param>
        /// <returns>A list of the labels of selected choices, or null if the prompt was canceled.</returns>
        Task<IReadOnlyList<string>> PromptMultipleSelection(string message, IReadOnlyList<PromptChoiceDetails> choices, IReadOnlyList<int> defaultChoiceIndexes);
    }

    internal class EditorUIService : IEditorUIService
    {
        private static string[] s_choiceResponseLabelSeparators = new[] { ", " };

        private readonly ILanguageServer _languageServer;

        public EditorUIService(ILanguageServer languageServer)
        {
            _languageServer = languageServer;
        }

        public Task<IReadOnlyList<string>> PromptMultipleSelection(string message, IReadOnlyList<PromptChoiceDetails> choices) =>
            PromptMultipleSelection(message, choices, defaultChoiceIndexes: null);

        public async Task<IReadOnlyList<string>> PromptMultipleSelection(string message, IReadOnlyList<PromptChoiceDetails> choices, IReadOnlyList<int> defaultChoiceIndexes)
        {
            ChoiceDetails[] choiceDetails = GetChoiceDetails(choices);

            ShowChoicePromptResponse response = await _languageServer.SendRequest<ShowChoicePromptRequest, ShowChoicePromptResponse>(
                "powerShell/showChoicePrompt",
                new ShowChoicePromptRequest
                {
                    IsMultiChoice = true,
                    Caption = string.Empty,
                    Message = message,
                    Choices = choiceDetails,
                    DefaultChoices = defaultChoiceIndexes?.ToArray(),
                });

            if (response.PromptCancelled)
            {
                return null;
            }

            return response.ResponseText.Split(s_choiceResponseLabelSeparators, StringSplitOptions.None);
        }

        public Task<string> PromptSelection(string message, IReadOnlyList<PromptChoiceDetails> choices) =>
            PromptSelection(message, choices, defaultChoiceIndex: -1);

        public async Task<string> PromptSelection(string message, IReadOnlyList<PromptChoiceDetails> choices, int defaultChoiceIndex)
        {
            ChoiceDetails[] choiceDetails = GetChoiceDetails(choices);

            ShowChoicePromptResponse response = await _languageServer.SendRequest<ShowChoicePromptRequest, ShowChoicePromptResponse>(
                "powerShell/showChoicePrompt",
                new ShowChoicePromptRequest
                {
                    IsMultiChoice = false,
                    Caption = string.Empty,
                    Message = message,
                    Choices = choiceDetails,
                    DefaultChoices = defaultChoiceIndex > -1 ? new[] { defaultChoiceIndex } : null,
                });

            if (response.PromptCancelled)
            {
                return null;
            }

            return response.ResponseText;
        }

        private static ChoiceDetails[] GetChoiceDetails(IReadOnlyList<PromptChoiceDetails> promptChoiceDetails)
        {
            var choices = new ChoiceDetails[promptChoiceDetails.Count];
            for (int i = 0; i < promptChoiceDetails.Count; i++)
            {
                choices[i] = new ChoiceDetails
                {
                    Label           = promptChoiceDetails[i].Label,
                    HelpMessage     = promptChoiceDetails[i].HelpMessage,
                    HotKeyIndex     = -1,
                    HotKeyCharacter = null,
                };
            }
            return choices;
        }
    }
}