using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Language.CodeLens;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using System;
using System.Threading.Tasks;
using NeopilotVS.Packets;

namespace NeopilotVS.Commands;

[Command(PackageIds.ExplainSelectionCodeBlock)]
internal class CommandExplainSelectionCodeBlock : BaseCommandCodeLens<CommandExplainSelectionCodeBlock>
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
                ITextSnapshotLine line =
                    docView.TextBuffer.CurrentSnapshot.GetLineFromPosition(startPos);
                int startLine = line.LineNumber;

                await ResolveCodeBlock(startLine);
                if (functionInfo != null)
                {
                    await controller.ExplainFunctionAsync(docView.FilePath, functionInfo);
                }
                else
                {
                    if (classInfo == null) return;
                    CodeBlockInfo codeBlockInfo = ClassToCodeBlock(classInfo);
                    await controller.ExplainCodeBlockAsync(
                        docView.FilePath, languageInfo.Type, codeBlockInfo);
                }
            }
        }
        catch (Exception ex)
        {
            await NeopilotVSPackage.Instance.LogAsync(ex.ToString());
        }
    }
}
