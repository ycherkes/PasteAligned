using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Windows;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Task = System.Threading.Tasks.Task;

namespace PasteAligned.Commands
{
    [Command(PackageIds.PasteAlignedCommand)]
    internal sealed class PasteAlignedCommand : BaseCommand<PasteAlignedCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            SnapshotPoint? position = docView?.TextView?.Selection.Start.Position;

            if (position.HasValue && docView.TextBuffer != null)
            {
                var (text, success) = GetClipboardText();

                var proceed = success && text.Split('\n').Length > 1;

                ITextView textView = docView.TextView;
                ITextSelection selection = textView.Selection;

                ITextSnapshotLine containingLine = selection.Start.Position.GetContainingLine();
                int count = position.Value - containingLine.Start.Position;

                proceed = proceed && count > 0;

                if (!proceed)
                {
                    return;
                }

                var whiteSpace = containingLine.Snapshot.GetText(new Span(0, count)).FirstOrDefault(char.IsWhiteSpace);

                if (!char.IsWhiteSpace(whiteSpace))
                {
                    whiteSpace = ' ';
                }
                else if(whiteSpace == '\t')
                {
                    var tabSize = textView.Options.GetOptionValue(DefaultOptions.TabSizeOptionId);
                    count /= tabSize;
                }

                var alignedString = text.Replace("\n", "\n" + new string(whiteSpace, count));

                ITextEdit edit = docView.TextBuffer.CreateEdit();
                Span span = selection.StreamSelectionSpan.SnapshotSpan;
                edit.Replace(span, alignedString);
                edit.Apply();
            }
        }

        private static (string text, bool success) GetClipboardText()
        {
            try
            {
                return (Clipboard.GetText(TextDataFormat.Text), true);
            }
            catch
            {
                return (string.Empty, false);
            }
        }

        protected override void BeforeQueryStatus(EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Command.Enabled = IsPasteCommandAvailable() && IsCSharpDocument();
            Command.Visible = Command.Enabled;
        }

        private static bool IsPasteCommandAvailable()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IVsCommandWindow commandWindow = ServiceProvider.GlobalProvider.GetService<SVsCommandWindow, IVsCommandWindow>();
            CommandID pasteCommand = commandWindow.PrepareCommand("Edit.Paste", out Guid pguidCmdGroup, out var pdwCmdID, out IntPtr _, Array.Empty<PREPARECOMMANDRESULT>()) != 0
                    ? null
                    : new CommandID(pguidCmdGroup, (int)pdwCmdID);

            return pasteCommand != null && IsCommandAvailable(pasteCommand);
        }

        private static bool IsCSharpDocument()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ServiceProvider.GlobalProvider.GetService<SVsTextManager, IVsTextManager>().GetActiveView(1, null, out IVsTextView ppView);
            if(ppView == null)
            {
                return false;
            }

            IWpfTextView wpfTextView = ServiceProvider.GlobalProvider.GetService<SComponentModel, IComponentModel2>()
                .GetService<IVsEditorAdaptersFactoryService>()?
                .GetWpfTextView(ppView);

            return wpfTextView?.TextBuffer?.ContentType.TypeName.Equals("CSharp") == true;
        }

        private static bool IsCommandAvailable(CommandID cmd)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IOleCommandTarget oleCommandTarget = ServiceProvider.GlobalProvider.GetService<SUIHostCommandDispatcher, IOleCommandTarget>();
            Guid guid = cmd.Guid;
            OLECMD[] oleCmdArray =
            {
                new()
                {
                    cmdID = (uint)cmd.ID,
                    cmdf = 0U
                }
            };

            return ErrorHandler.Succeeded(oleCommandTarget.QueryStatus(ref guid, (uint)oleCmdArray.Length, oleCmdArray, IntPtr.Zero)) && ((OLECMDF)oleCmdArray[0].cmdf).HasFlag(OLECMDF.OLECMDF_ENABLED);
        }
    }
}
