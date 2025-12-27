using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Language.CodeLens;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using System;
using System.Threading.Tasks;
using NeopilotVS.LanguageServer;

namespace NeopilotVS.Commands;

[Command(PackageIds.GenerateSelectionFunctionDocstring)]
internal class CommandGenerateSelectionFunctionDocstring
    : BaseCommandCodeLens<CommandGenerateSelectionFunctionDocstring>
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
                    await controller.GenerateFunctionDocstringAsync(docView.FilePath, functionInfo);
                }
            }
        }
        catch (Exception ex)
        {
            await NeopilotVSPackage.Instance.LogAsync(ex.ToString());
        }
    }
}
