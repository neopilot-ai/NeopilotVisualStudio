using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;
using NeopilotVS.Packets;

namespace NeopilotVS.Commands;

[Command(PackageIds.ExplainCodeBlock)]
internal class CommandExplainCodeBlock : BaseCommandContextMenu<CommandExplainCodeBlock>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        base.BeforeQueryStatus(e);
        if (Command.Visible)
            Command.Text =
                is_function ? "Neopilot: Explain Function" : "Neopilot: Explain Code block";
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        LanguageServerController controller =
            (Package as NeopilotVSPackage).LanguageServer.Controller;

        if (is_function)
        {
            FunctionInfo? functionInfo = await GetFunctionInfoAsync();

            if (functionInfo != null)
                await controller.ExplainFunctionAsync(docView.Document.FilePath, functionInfo);
        }
        else
        {
            CodeBlockInfo codeBlockInfo = GetCodeBlockInfo();
            await controller.ExplainCodeBlockAsync(
                docView.Document.FilePath, languageInfo.Type, codeBlockInfo);
        }
    }
}
