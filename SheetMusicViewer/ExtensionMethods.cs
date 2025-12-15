using System.Windows;
using System.Windows.Controls;
using SheetMusicLib;

namespace SheetMusicViewer
{
    /// <summary>
    /// WPF-specific extension methods
    /// </summary>
    public static class ExtensionMethods
    {
        // Note: FindIndexOfFirstGTorEQTo is now in SheetMusicLib.CollectionExtensions
        // It's available via 'using SheetMusicLib;'

        public static MenuItem AddMnuItem(this ContextMenu ctxmenu, string name, string tip, RoutedEventHandler hndlr)
        {
            var mitem = new MenuItem()
            {
                Header = name,
                ToolTip = tip
            };
            ctxmenu.Items.Add(mitem);
            mitem.Click += hndlr;
            return mitem;
        }
    }
}
