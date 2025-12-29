using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;
using NeopilotVS.Packets;

namespace NeopilotVS.Commands;

[Command(PackageIds.GenerateFunctionUnitTest)]
internal class CommandGenerateFunctionUnitTest
    : BaseCommandContextMenu<CommandGenerateFunctionUnitTest>
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
            await controller.GenerateFunctionUnitTestAsync(
                "Generate unit test", docView.Document.FilePath, functionInfo);
    }
}
