using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;
using NeopilotVS.Windows;

namespace NeopilotVS.Commands;

[Command(PackageIds.OpenChatWindow)]
internal sealed class CommandOpenChatWindow : BaseCommand<CommandOpenChatWindow>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        ToolWindowPane toolWindowPane = await NeopilotVSPackage.Instance.ShowToolWindowAsync(
            typeof(ChatToolWindow), 0, create: true, NeopilotVSPackage.Instance.DisposalToken);
        if (toolWindowPane == null || toolWindowPane.Frame == null)
        {
            throw new NotSupportedException("Cannot create Neopilot chat tool window");
        }
    }
}
