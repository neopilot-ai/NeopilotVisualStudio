using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;

namespace NeopilotVS.Commands;

[Command(PackageIds.ShowNextSuggestion)]
internal sealed class CommandShowNextSuggestion
    : BaseCommandCompletionHandler<CommandShowNextSuggestion>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        try
        {
            if (completionHandler == null) return;

            completionHandler.ShowNextSuggestion();
        }
        catch (Exception ex)
        {
            await NeopilotVSPackage.Instance.LogAsync(
                $"CommandShowNextSuggestion: Failed to show next suggestion; Exception: {ex}");
        }
    }
}
