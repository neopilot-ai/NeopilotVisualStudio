using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Diagnostics;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using NeopilotVS.Utilities;
using NeopilotVS.SuggestionUI; // Assuming NeopilotCompletionHandler is here or needs it

namespace NeopilotVS.Commands;

internal class BaseCommandCompletionHandler<T> : BaseCommand<T>
    where T : class, new()
{
    internal static long lastQuery = 0;
    protected static DocumentView? docView;
    protected static NeopilotCompletionHandler? completionHandler;
    protected override void BeforeQueryStatus(EventArgs e)
    {
        // Derived menu commands will call this repeatedly upon openning
        // so we only want to do it once, i can't find a better way to do it
        long timeStamp = Stopwatch.GetTimestamp();
        if (lastQuery != 0 && timeStamp - lastQuery < 500) return;
        lastQuery = timeStamp;

        ThreadHelper.JoinableTaskFactory.Run(async delegate {
            docView = await ViewUtils.GetActiveDocumentViewAsync();
            if (docView?.TextView == null) return false;

            try
            {
                var key = typeof(NeopilotCompletionHandler);
                var props = docView.TextBuffer.Properties;
                if (props.ContainsProperty(key))
                {
                    completionHandler = props.GetProperty<NeopilotCompletionHandler>(key);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                await NeopilotVSPackage.Instance.LogAsync(
                    $"BaseCommandDocView: Failed to check properties; Exception: {ex}");
                return false;
            }
        });
    }
}
