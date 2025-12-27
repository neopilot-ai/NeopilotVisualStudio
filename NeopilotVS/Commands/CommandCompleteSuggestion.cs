using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;

namespace NeopilotVS.Commands;

[Command(PackageIds.CompleteSuggestion)]
internal sealed class CommandCompleteSuggestion
    : BaseCommandCompletionHandler<CommandCompleteSuggestion>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        try
        {
            if (completionHandler == null) return;
            completionHandler.CompleteSuggestion();
        }
        catch (Exception ex)
        {
            await NeopilotVSPackage.Instance.LogAsync(
                $"CommandShowNextSuggestion: Failed to complete suggestion; Exception: {ex}");
        }
    }
}
