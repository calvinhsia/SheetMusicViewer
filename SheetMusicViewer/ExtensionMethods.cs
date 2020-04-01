using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SheetMusicViewer
{
    public static class ExtensionMethods
    {
        /// <summary>
        /// Binary search for 1st item >= key
        /// Returns -1 for empty list
        /// Returns list.count if key > all items
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sortedList"></param>
        /// <param name="key"></param>
        public static int FindIndexOfFirstGTorEQTo<T>(this IList<T> sortedList, T key) where T : IComparable<T>
        {
            int right;
            if (sortedList.Count == 0) //empty list
            {
                right = -1;
            }
            else
            {
                right = sortedList.Count - 1;
                int left = 0;
                while (right > left)
                {
                    var ndx = (left + right) / 2;
                    var elem = sortedList[ndx];
                    if (elem.CompareTo(key) >= 0)
                    {
                        right = ndx;
                    }
                    else
                    {
                        left = ndx + 1;
                    }
                }
            }
            if (right >= 0) // see if we're beyond the list?
            {
                if (sortedList[right].CompareTo(key) < 0)
                {
                    right = sortedList.Count;
                }
            }
            return right;
        }

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
