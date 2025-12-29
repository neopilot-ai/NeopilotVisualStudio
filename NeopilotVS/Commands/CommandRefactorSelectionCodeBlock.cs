using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Language.CodeLens;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text;
using System;
using System.Threading.Tasks;
using System.Windows;
using NeopilotVS.Packets;
using NeopilotVS.Utilities;

namespace NeopilotVS.Commands;

[Command(PackageIds.RefactorSelectionCodeBlock)]
internal class CommandRefactorSelectionCodeBlock
    : BaseCommandCodeLens<CommandRefactorSelectionCodeBlock>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {

        try
        {
            LanguageServerController controller =
                (Package as NeopilotVSPackage).LanguageServer.Controller;

            if (e.InValue is CodeLensDescriptorContext ctx)
            {
                await NeopilotVSPackage.Instance.LogAsync(e.InValue.ToString());
                int startPos = ctx.ApplicableSpan.Value.Start;
                ITextSnapshotLine snapshotLine = docView.TextBuffer.CurrentSnapshot.GetLineFromPosition(startPos);
                int startLine = snapshotLine.LineNumber;
                TextBounds selectionLine =
                    docView.TextView.TextViewLines.GetCharacterBounds(snapshotLine.Start);
                Point selectionScreenPos = docView.TextView.VisualElement.PointToScreen(
                    new Point(selectionLine.Left - docView.TextView.ViewportLeft,
                              selectionLine.Top - docView.TextView.ViewportTop));

                // highlight the selected codeblock
                TextHighlighter? highlighter = TextHighlighter.GetInstance(docView.TextView);
                highlighter?.AddHighlight(snapshotLine.Extent);

                var dialog = RefactorCodeDialogWindow.GetOrCreate();
                string? prompt = await dialog.ShowAndGetPromptAsync(
                    languageInfo, selectionScreenPos.X, selectionScreenPos.Y);

                highlighter?.ClearAll();

                await ResolveCodeBlock(startLine);
                // user did not select any of the prompt
                if (prompt == null) return;
                if (functionInfo != null)
                {
                    await controller.RefactorFunctionAsync(prompt, docView.FilePath, functionInfo);
                }
                else
                {
                    if (classInfo == null) return;
                    CodeBlockInfo codeBlockInfo = ClassToCodeBlock(classInfo);

                    await controller.RefactorCodeBlockAsync(
                        prompt, docView.FilePath, languageInfo.Type, codeBlockInfo);
                }
            }
        }
        catch (Exception ex)
        {
            await NeopilotVSPackage.Instance.LogAsync(ex.ToString());
        }
    }
}
