using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System.Threading.Tasks;
using NeopilotVS.Packets;

namespace NeopilotVS.Commands;

[Command(PackageIds.FindSimilarSelectionCodeBlock)]
internal class CommandFindSimilarSelectionCodeBlock : BaseCommandCodeLens<CommandFindSimilarSelectionCodeBlock>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        LanguageServerController controller =
            (Package as NeopilotVSPackage).LanguageServer.Controller;

        var (docView, functionInfo) = await GetFunctionInfoAsync(e);
        if (functionInfo == null) return;

        CodeBlockInfo codeBlockInfo = new CodeBlockInfo {
            raw_source = functionInfo.RawSource,
            start_line = functionInfo.DefinitionLine, // Best effort
            end_line = functionInfo.DefinitionLine + functionInfo.RawSource.Split('\n').Length
        };

        await controller.FindSimilarCodeAsync(docView.Document.FilePath, functionInfo.Language, codeBlockInfo);
    }
}
