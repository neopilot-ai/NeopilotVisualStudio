using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Text.Editor;

namespace NeopilotVS.Utilities
{
    internal static class ViewUtils
    {
        /// <summary>
        /// Retrieves the currently active document view safely on the main thread.
        /// </summary>
        /// <returns>The active DocumentView, or null if an error occurs.</returns>
        public static async Task<DocumentView?> GetActiveDocumentViewAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                return await VS.Documents.GetActiveDocumentViewAsync();
            }
            catch (Exception ex)
            {
                await NeopilotVSPackage.Instance.LogAsync($"Failed to get active document view: {ex}");
                return null;
            }
        }
    }
}
