using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Diagnostics;
using System.Windows;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using NeopilotVS.Packets;
using NeopilotVS.Utilities;
 // For LanguageServerController if needed

namespace NeopilotVS.Commands;

// this class provide the code context for the 4 right-click menu commands
// otherwise, those commands will need to do the same thing repeatedly
// this is rather ugly, but it works
internal class BaseCommandContextMenu<T> : BaseCommand<T>
    where T : class, new()
{
    internal static long lastQuery = 0;
    internal static bool is_visible = false;

    protected static DocumentView? docView;
    protected static string text; // the selected text
    protected static bool is_function = false;
    protected static int start_line, end_line;
    protected static int start_col, end_col;
    protected static int start_position, end_position;
    protected static Languages.LangInfo languageInfo;

    protected override void BeforeQueryStatus(EventArgs e)
    {
        Command.Visible = is_visible;

        // Derived menu commands will call this repeatedly upon openning
        // so we only want to do it once, i can't find a better way to do it
        long timeStamp = Stopwatch.GetTimestamp();
        if (lastQuery != 0 && timeStamp - lastQuery < 500) return;
        lastQuery = timeStamp;

        // If there are no selection, and we couldn't find any block that the caret is in
        // then we don't want to show the command
        is_visible = Command.Visible = ThreadHelper.JoinableTaskFactory.Run(async delegate {
            is_function = false;

            try
            {
                docView = await ViewUtils.GetActiveDocumentViewAsync();
                if (docView?.TextView == null) return false;
            }
            catch (Exception ex)
            {
                await NeopilotVSPackage.Instance.LogAsync(
                    $"BaseCommandContextMenu: Failed to get the active document view; Exception: {ex}");
                return false;
            }

            languageInfo = Languages.Mapper.GetLanguage(docView);
            ITextSelection selection = docView.TextView.Selection;

            start_position = selection.Start.Position;
            end_position = selection.End.Position;

            // if there is no selection, attempt to get the code block at the caret
            if (selection.SelectedSpans.Count == 0 || start_position == end_position)
            {
                Span blockSpan = CodeAnalyzer.GetBlockSpan(
                    docView.TextView, selection.Start.Position.Position, out var tag);
                if (tag == null) return false;

                start_position = blockSpan.Start;
                end_position = blockSpan.End;

                // "Type"          | class, struct, enum
                // "Member"        | function
                // "Namespace"     | namespace
                // "Expression"    | lambda
                // "Nonstructural" | nothing
                is_function = tag?.Type == "Member";
            }

            ITextSnapshotLine selectionStart =
                docView.TextBuffer.CurrentSnapshot.GetLineFromPosition(start_position);
            ITextSnapshotLine selectionEnd =
                docView.TextBuffer.CurrentSnapshot.GetLineFromPosition(end_position);

            start_line = selectionStart.LineNumber + 1;
            end_line = selectionEnd.LineNumber + 1;
            start_col = start_position - selectionStart.Start.Position + 1;
            end_col = end_position - selectionEnd.Start.Position + 1;

            text = docView.TextBuffer.CurrentSnapshot.GetText(start_position,
                                                              end_position - start_position);

            return true;
        });
    }

    protected async Task<FunctionInfo?> GetFunctionInfoAsync()
    {
        FunctionBlock? func =
            await CodeAnalyzer.GetFunctionBlockAsync(docView.TextView, start_line, start_col);

        if (func == null)
        {
            await NeopilotVSPackage.Instance.LogAsync("Error: Could not get function info");
            return null;
        }

        return new() {
            RawSource = text,
            CleanFunction = text,
            NodeName = func.Name,
            Params = func.Params,
            DefinitionLine = start_line,
            StartLine = start_line,
            EndLine = end_line,
            StartCol = start_col,
            EndCol = end_col,
            Language = languageInfo.Type,
        };
    }

    protected CodeBlockInfo GetCodeBlockInfo()
    {
        return new() {
            raw_source = text,
            start_line = start_line,
            end_line = end_line,
            start_col = start_col,
            end_col = end_col,
        };
    }
}
