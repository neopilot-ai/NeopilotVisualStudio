using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Formatting;
using System;
using System.Threading.Tasks;
using System.Windows;
using NeopilotVS.LanguageServer;
using NeopilotVS.Packets;
using NeopilotVS.Utilities;
using NeopilotVS.Windows;

namespace NeopilotVS.Commands;

[Command(PackageIds.RefactorCodeBlock)]
internal class CommandRefactorCodeBlock : BaseCommandContextMenu<CommandRefactorCodeBlock>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        base.BeforeQueryStatus(e);
        if (Command.Visible)
            Command.Text =
                is_function ? "Neopilot: Refactor Function" : "Neopilot: Refactor Code block";
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        // get the caret screen position and create the dialog at that position
        TextBounds caretLine = docView.TextView.TextViewLines.GetCharacterBounds(
            docView.TextView.Caret.Position.BufferPosition);
        Point caretScreenPos = docView.TextView.VisualElement.PointToScreen(
            new Point(caretLine.Left - docView.TextView.ViewportLeft,
                      caretLine.Top - docView.TextView.ViewportTop));

        // highlight the selected codeblock
        TextHighlighter? highlighter = TextHighlighter.GetInstance(docView.TextView);
        highlighter?.AddHighlight(start_position, end_position - start_position);
        var dialog = RefactorCodeDialogWindow.GetOrCreate();
        string? prompt =
            await dialog.ShowAndGetPromptAsync(languageInfo, caretScreenPos.X, caretScreenPos.Y);

        highlighter?.ClearAll();

        // user did not select any of the prompt
        if (prompt == null) return;

        LanguageServerController controller =
            (Package as NeopilotVSPackage).LanguageServer.Controller;
        if (is_function)
        {
            FunctionInfo? functionInfo = await GetFunctionInfoAsync();

            if (functionInfo != null)
                await controller.RefactorFunctionAsync(
                    prompt, docView.Document.FilePath, functionInfo);
        }
        else
        {
            CodeBlockInfo codeBlockInfo = GetCodeBlockInfo();
            await controller.RefactorCodeBlockAsync(
                prompt, docView.Document.FilePath, languageInfo.Type, codeBlockInfo);
        }
    }
}
