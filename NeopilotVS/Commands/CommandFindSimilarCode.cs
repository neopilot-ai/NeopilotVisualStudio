using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;
using NeopilotVS.LanguageServer;
using NeopilotVS.Packets;

namespace NeopilotVS.Commands;

[Command(PackageIds.FindSimilarCode)]
internal class CommandFindSimilarCode : BaseCommandContextMenu<CommandFindSimilarCode>
{
    protected override void BeforeQueryStatus(EventArgs e)
    {
        base.BeforeQueryStatus(e);
        if (Command.Visible)
            Command.Text = "Neopilot: Find Similar Code";
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        LanguageServerController controller =
            (Package as NeopilotVSPackage).LanguageServer.Controller;

        CodeBlockInfo codeBlockInfo;
        Language language = languageInfo.Type;

        if (is_function)
        {
            FunctionInfo? functionInfo = await GetFunctionInfoAsync();
            if (functionInfo == null) return;
            codeBlockInfo = new CodeBlockInfo {
                raw_source = functionInfo.RawSource,
                start_line = functionInfo.DefinitionLine, // Best effort
                end_line = functionInfo.DefinitionLine + functionInfo.RawSource.Split('\n').Length
            };
            language = functionInfo.Language;
        }
        else
        {
            codeBlockInfo = GetCodeBlockInfo();
        }

        await controller.FindSimilarCodeAsync(docView.Document.FilePath, language, codeBlockInfo);
    }
}
