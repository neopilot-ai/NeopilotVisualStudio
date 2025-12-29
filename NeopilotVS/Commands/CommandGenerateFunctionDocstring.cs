using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;
using NeopilotVS.Packets;

namespace NeopilotVS.Commands;

[Command(PackageIds.GenerateFunctionDocstring)]
internal class CommandGenerateFunctionDocstring
    : BaseCommandContextMenu<CommandGenerateFunctionDocstring>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        base.BeforeQueryStatus(e);
        Command.Visible = is_function;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        LanguageServerController controller =
            (Package as NeopilotVSPackage).LanguageServer.Controller;
        FunctionInfo? functionInfo = await GetFunctionInfoAsync();

        if (functionInfo != null)
            await controller.GenerateFunctionDocstringAsync(docView.Document.FilePath,
                                                            functionInfo);
    }
}
