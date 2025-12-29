using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using NeopilotVS.Packets;
using NeopilotVS.Utilities;

namespace NeopilotVS.Commands;

internal class BaseCommandCodeLens<T> : BaseCommand<T>
    where T : class, new()
{
    internal static long lastQuery = 0;

    protected static DocumentView? docView;
    protected static string text; // the selected text
    protected static Languages.LangInfo languageInfo;
    protected FunctionInfo functionInfo;
    protected ClassInfo classInfo;
    protected override void BeforeQueryStatus(EventArgs e)
    {
        // Derived menu commands will call this repeatedly upon openning
        // so we only want to do it once, i can't find a better way to do it
        long timeStamp = Stopwatch.GetTimestamp();
        if (lastQuery != 0 && timeStamp - lastQuery < 500) return;
        lastQuery = timeStamp;

        ThreadHelper.JoinableTaskFactory.Run(async delegate {
            try
            {
                docView = await ViewUtils.GetActiveDocumentViewAsync();
                if (docView?.TextView == null) return false;
                text = docView.TextBuffer.CurrentSnapshot.GetText();
            }
            catch (Exception ex)
            {
                await NeopilotVSPackage.Instance.LogAsync(
                    $"BaseCommandContextMenu: Failed to get the active document view; Exception: {ex}");
                return false;
            }

            languageInfo = Languages.Mapper.GetLanguage(docView);
            ITextSelection selection = docView.TextView.Selection;

            return true;
        });
    }

    protected async Task<bool> ResolveCodeBlock(int startLine)
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        IList<Packets.FunctionInfo>? functions =
            await NeopilotVSPackage.Instance.LanguageServer.GetFunctionsAsync(
                docView.FilePath, text, languageInfo, 0, cts.Token);

        IList<Packets.ClassInfo>? classes =
            await NeopilotVSPackage.Instance.LanguageServer.GetClassInfosAsync(
                docView.FilePath,
                text,
                languageInfo,
                0,
                docView.TextView.Options.GetOptionValue(DefaultOptions.NewLineCharacterOptionId),
                cts.Token);

        FunctionInfo minFunction = null;
        int minDistance = int.MaxValue;
        foreach (var f in functions)
        {
            var distance = Math.Abs(f.DefinitionLine - startLine);
            if (distance < minDistance)
            {
                minDistance = distance;
                functionInfo = f;
            }
        }

        foreach (var c in classes)
        {
            var distance = Math.Abs(c.StartLine - startLine);
            if (distance < minDistance)
            {
                minDistance = distance;
                functionInfo = null;
                classInfo = c;
            }
        }
        return true;
    }

    protected CodeBlockInfo ClassToCodeBlock(ClassInfo c)
    {
        CodeBlockInfo codeBlockInfo = null;
        try
        {
            var snapshotLineStart =
                docView.TextBuffer.CurrentSnapshot.GetLineFromLineNumber(c.StartLine);
            var snapShotLineEnd =
                docView.TextBuffer.CurrentSnapshot.GetLineFromLineNumber(c.EndLine);

            var start_position = snapshotLineStart.Start;
            var end_position = snapShotLineEnd.End;

            var start_line = snapshotLineStart.LineNumber + 1;
            var end_line = snapShotLineEnd.LineNumber + 1;
            var start_col = start_position - snapshotLineStart.Start.Position + 1;
            var end_col = end_position - snapShotLineEnd.Start.Position + 1;

            text = docView.TextBuffer.CurrentSnapshot.GetText(start_position,
                                                              end_position - start_position);

            codeBlockInfo = new() {
                raw_source = text,
                start_line = start_line,
                end_line = end_line,
                start_col = start_col,
                end_col = end_col,
            };
        }
        catch (Exception ex)
        {
            Task.Run(async () =>
                     { return NeopilotVSPackage.Instance.LogAsync(ex.ToString()); });
        }
        return codeBlockInfo;
    }
}
