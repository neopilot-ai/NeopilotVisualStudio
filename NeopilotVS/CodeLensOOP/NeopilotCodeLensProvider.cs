
using NeopilotVS.Packets;
using NeopilotVS.Properties;
using Microsoft.VisualStudio.Language.CodeLens;
using Microsoft.VisualStudio.Language.CodeLens.Remoting;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using System.Windows.Markup;
using WebSocketSharp;

namespace NeopilotVS
{
[Export(typeof(IAsyncCodeLensDataPointProvider))]
[Name(Id)]
[ContentType("text")]
[LocalizedName(typeof(Resources), "NeopilotCodeLensProvider")]
[Priority(200)]

internal class NeopilotCodeLensProvider : IAsyncCodeLensDataPointProvider
{
    internal const string Id = "NeopilotCodeLens";
    private readonly Lazy<ICodeLensCallbackService> callbackService;

    [ImportingConstructor]
    public NeopilotCodeLensProvider(Lazy<ICodeLensCallbackService> callbackService)
    {
        this.callbackService = callbackService;
    }

    public async Task<bool> CanCreateDataPointAsync(CodeLensDescriptor descriptor,
                                                    CodeLensDescriptorContext context,
                                                    CancellationToken token)
    {

        try
        {
            var callback = callbackService.Value;
            var name = nameof(ICodeLensListener.IsNeopilotCodeLensActive);

            var res = await callback.InvokeAsync<bool>(this, name).Caf();

            return res;
        }
        catch (Exception ex)
        {
            CodelensLogger.LogCL($"CanCreateDataPointAsync failed: {ex}");
            return false;
        }
    }

    public async Task<IAsyncCodeLensDataPoint>
    CreateDataPointAsync(CodeLensDescriptor descriptor, CodeLensDescriptorContext context,
                         CancellationToken token)
    {
        NeopilotDataPoint dp = null;
        try
        {
            dp = new NeopilotDataPoint(callbackService.Value, descriptor);
        }
        catch (Exception ex)
        {
            CodelensLogger.LogCL($"CreateDataPointAsync failed to create DataPoint: {ex}");
            return null;
        }

        try
        {
            var callback = callbackService.Value;
            var name = nameof(ICodeLensListener.GetVisualStudioPid);

            var vspid = await callback.InvokeAsync<int>(this, name).Caf();

            await dp.ConnectToVisualStudio(vspid).Caf();
        }
        catch (Exception ex)
        {
            CodelensLogger.LogCL(ex.ToString());
        }

        return dp;
    }
}

public class NeopilotDataPoint : IAsyncCodeLensDataPoint
{
    private static readonly CodeLensDetailEntryCommand refactorCommand =
        new CodeLensDetailEntryCommand { CommandSet = PackageGuids.NeopilotVS,
                                         CommandId = 0x0110,
                                         CommandName = "Neopilot.RefactorSelectionCodeBlock" };

    private static readonly CodeLensDetailEntryCommand explainCommand =
        new CodeLensDetailEntryCommand { CommandSet = PackageGuids.NeopilotVS,
                                         CommandId = 0x0111,
                                         CommandName = "Neopilot.ExplainSelectionCodeBlock" };
    private static readonly CodeLensDetailEntryCommand goDocCommand =
        new CodeLensDetailEntryCommand { CommandSet = PackageGuids.NeopilotVS,
                                         CommandId = 0x0112,
                                         CommandName =
                                             "Neopilot.GenerateSelectionFunctionDocstring" };

    private VisualStudioConnectionHandler? visualStudioConnection;

    private readonly ManualResetEventSlim dataLoaded =
        new ManualResetEventSlim(initialState: false);

    private readonly CodeLensDescriptor descriptor;
    public readonly Guid id = Guid.NewGuid();
    private readonly ICodeLensCallbackService callbackService;
    private volatile FunctionInfo data;

    public NeopilotDataPoint(ICodeLensCallbackService callbackService, CodeLensDescriptor descriptor)
    {
        this.descriptor = descriptor;
        this.callbackService = callbackService;
    }

    public event AsyncEventHandler InvalidatedAsync;

    public CodeLensDescriptor Descriptor => this.descriptor;

    public async Task ConnectToVisualStudio(int vspid) => visualStudioConnection =
        await VisualStudioConnectionHandler.Create(owner: this, vspid).Caf();

    public async Task<CodeLensDataPointDescriptor> GetDataAsync(CodeLensDescriptorContext context,
                                                                CancellationToken token)
    {
        try
        {

            if (DoesNeedGoDocData())
            {
                try
                {
                    data = await LoadInstructions(context, token).Caf();
                    dataLoaded.Set();
                }
                catch (Exception ex)
                {
                    CodelensLogger.LogCL(ex.ToString());
                }
            }

            CodeLensDataPointDescriptor response = new CodeLensDataPointDescriptor() {
                Description = "Invoke Neopilot",
                TooltipText = "Neopilot",
                IntValue = null, // no int value
            };

            return response;
        }
        catch (Exception ex)
        {
            CodelensLogger.LogCL(ex.ToString());

            return null;
        }
    }

    // Called from VS via JSON RPC.
    public void Refresh() => _ = InvalidatedAsync?.InvokeAsync(this, EventArgs.Empty);

    bool DoesNeedGoDocData()
    {
        if (descriptor == null) return false;

        return (descriptor.Kind is CodeElementKinds.Function ||
                descriptor.Kind is CodeElementKinds.Method);
    }

    public async Task<CodeLensDetailsDescriptor> GetDetailsAsync(CodeLensDescriptorContext context,
                                                                 CancellationToken token)
    {
        if (DoesNeedGoDocData())
        {
            try
            {
                if (!dataLoaded.Wait(timeout: TimeSpan.FromSeconds(.5), token))
                {
                    data = await LoadInstructions(context, token).Caf();
                    dataLoaded.Set();
                }
            }
            catch (Exception ex)
            {
                CodelensLogger.LogCL(ex.ToString());
            }
        }

        try
        {
            var l = new List<CodeLensDetailPaneCommand>() {
                new CodeLensDetailPaneCommand() { CommandId = refactorCommand,
                                                  CommandDisplayName = "Refactor",
                                                  CommandArgs = new object[] { context } },
                new CodeLensDetailPaneCommand() { CommandId = explainCommand,
                                                  CommandDisplayName = "Explain",
                                                  CommandArgs = new object[] { context } },
            };

            if (data != null)
            {
                if (data.Docstring.IsNullOrEmpty())
                {
                    l.Add(
                        new CodeLensDetailPaneCommand() { CommandId = goDocCommand,
                                                          CommandDisplayName = "Generate Docstring",
                                                          CommandArgs = new object[] { context } });
                }
                else { CodelensLogger.LogCL("GetDetailsAsync Docstring" + data.Docstring); }
            }

            var result = new CodeLensDetailsDescriptor() {
                PaneNavigationCommands = l,
            };

            return result;
        }
        catch (Exception ex)
        {
            CodelensLogger.LogCL(ex.ToString());
            return null;
        }
    }

    private async Task<FunctionInfo> LoadInstructions(CodeLensDescriptorContext ctx,
                                                      CancellationToken ct) =>
        await callbackService
            .InvokeAsync<FunctionInfo>(
                this, nameof(ICodeLensListener.LoadInstructions), new object[] {
                    id,
                    Descriptor.ProjectGuid,
                    Descriptor.FilePath,
                    ctx.ApplicableSpan !=
                        null? ctx.ApplicableSpan.Value.Start : throw new InvalidOperationException(
                                                           $"No ApplicableSpan."),
                    ctx.ApplicableSpan!.Value.Length
                },
                ct)
            .Caf();

    /// <summary>
    /// Raises <see cref="IAsyncCodeLensDataPoint.Invalidated"/> event.
    /// </summary>
    /// <remarks>
    ///  This is not part of the IAsyncCodeLensDataPoint interface.
    ///  The data point source can call this method to notify the client proxy that data for this
    ///  data point has changed.
    /// </remarks>
    public void Invalidate()
    {
        this.InvalidatedAsync?.Invoke(this, EventArgs.Empty).ConfigureAwait(false);
    }
}

}
