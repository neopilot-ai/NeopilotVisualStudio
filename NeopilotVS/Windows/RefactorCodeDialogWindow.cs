using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.PlatformUI;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System;

namespace NeopilotVS;

public partial class RefactorCodeDialogWindow : DialogWindow
{
    private class RefactorData(string text, ImageMoniker image, string? prompt = null,
                               List<Packets.Language>? whiteListLanguages = null)
    {
        public string text = text;
        public string prompt = prompt ?? text;
        public ImageMoniker image = image;
        public List<Packets.Language>? whiteListLanguages = whiteListLanguages;
    };

    private string? Result = null;
    private static RefactorCodeDialogWindow? Instance = null;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseDialog();
    private void SendButton_Click(object sender, RoutedEventArgs e) => ReturnResult(InputPrompt.Text);

    public RefactorCodeDialogWindow()
    {
        InitializeComponent();
        InputPrompt.LostKeyboardFocus += (s, e) => { InputPrompt.Focus(); };
    }

    public static RefactorCodeDialogWindow GetOrCreate()
    {
        return Instance ??= new RefactorCodeDialogWindow();
    }

    public async Task<string?> ShowAndGetPromptAsync(Languages.LangInfo languageInfo,
                                                     double? x = null, double? y = null)
    {
        NewContext(languageInfo);

        if (x != null) Left = x.Value;
        if (y != null) Top = y.Value;

        await this.ShowDialogAsync();
        return Result;
    }

    private void CloseDialog()
    {
        Visibility = Visibility.Hidden;
        InputPrompt.Text = string.Empty;
    }

    private void NewContext(Languages.LangInfo languageInfo)
    {
        Result = null;

        RefactorData[] commandPresets = [
            new RefactorData("Add comments and docstrings", KnownMonikers.AddComment),
            new RefactorData("Add logging for debugging", KnownMonikers.StartLogging),
            new RefactorData("Generate Unit Tests", KnownMonikers.UnitTest,
                             "Generate unit tests for this code using the most appropriate testing framework. Cover edge cases and happy paths."),
            new RefactorData("Clean up and standardize code", KnownMonikers.CleanData,
                             "Clean up this code by standardizing variable names, removing debugging statements, and improving readability."),
            new RefactorData("Check for bugs and null pointers", KnownMonikers.Spy,
                             "Check for bugs such as null pointer references and unhandled exceptions."),
            new RefactorData("Implement code for TODO comments", KnownMonikers.ImplementInterface),
            new RefactorData("Fix linter errors and warnings", KnownMonikers.DocumentWarning, null, [Packets.Language.LANGUAGE_PYTHON]),
            new RefactorData("Optimize performance", KnownMonikers.EventPublic),
            new RefactorData("Convert to async / await", KnownMonikers.AsynchronousMessage, null,
                             [Packets.Language.LANGUAGE_TYPESCRIPT, Packets.Language.LANGUAGE_JAVASCRIPT, Packets.Language.LANGUAGE_TSX]),
            new RefactorData("Add detailed explanations", KnownMonikers.CommentCode),
        ];

        // Clear existing preset buttons safely (keep the "PRESETS" title if it's the first child)
        if (PresetsPanel != null)
        {
            while (PresetsPanel.Children.Count > 1)
            {
                PresetsPanel.Children.RemoveAt(1);
            }

            Style? buttonStyle = Resources["ModernButtonStyle"] as Style;

            foreach (RefactorData data in commandPresets)
            {
                if (data.whiteListLanguages != null && !data.whiteListLanguages.Contains(languageInfo.Type))
                    continue;

                StackPanel btnContent = new() { Orientation = Orientation.Horizontal };
                btnContent.Children.Add(new CrispImage() { Moniker = data.image, Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0) });
                btnContent.Children.Add(new TextBlock() { Text = data.text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });

                Button btn = new() 
                { 
                    Content = btnContent,
                    Style = buttonStyle,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                btn.Click += (s, e) => { ReturnResult(data.prompt); };

                PresetsPanel.Children.Add(btn);
            }
        }
    }

    private void ReturnResult(string result)
    {
        if (string.IsNullOrWhiteSpace(result)) return;
        Result = result;
        CloseDialog();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        // set dark title bar
        bool value = true;
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        DwmSetWindowAttribute(hwnd, 20, ref value, System.Runtime.InteropServices.Marshal.SizeOf(value));
    }

    protected override void OnDeactivated(EventArgs e) { CloseDialog(); }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref bool attrValue, int attrSize);

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { DragMove(); }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) CloseDialog();
    }

    private void InputPrompt_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (InputPromptHint == null || PresetsPanel == null) return;

        bool hasText = !string.IsNullOrEmpty(InputPrompt.Text);
        InputPromptHint.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;

        string search = InputPrompt.Text.ToLower();

        for (int i = 1; i < PresetsPanel.Children.Count; i++)
        {
            if (PresetsPanel.Children[i] is Button btn && btn.Content is StackPanel sp)
            {
                var textBlock = sp.Children.OfType<TextBlock>().FirstOrDefault();
                if (textBlock != null)
                {
                    btn.Visibility = string.IsNullOrEmpty(search) || textBlock.Text.ToLower().Contains(search) 
                                     ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    private void InputPrompt_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) { ReturnResult(InputPrompt.Text); }
    }
}
