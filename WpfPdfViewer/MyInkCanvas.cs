using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfPdfViewer
{
    public class MyInkCanvas : InkCanvas
    {
        private readonly PdfViewerWindow _pdfViewerWindow;
        readonly BitmapImage _bmImage;
        readonly int _PgNo;
        Size _availSize;

        public MyInkCanvas(BitmapImage bmImage, PdfViewerWindow pdfViewerWindow, CheckBox chkInk, int PgNo)
        {
            this._pdfViewerWindow = pdfViewerWindow;
            this._PgNo = PgNo;
            _bmImage = bmImage;
            if (chkInk.IsChecked == false)
            {
                this.EditingMode = InkCanvasEditingMode.None; // if we're not inking, then let mouse events change the page
            }
            else
            {
                AddCtxtMenu();
            }
            //var imBrush = new ImageBrush(bmImage);
            //Background = imBrush;
            //Background = Brushes.AliceBlue;
            //var im = new Image() { Source = bmImage };
            //this.Children.Add(im);
            this.Loaded += (o, e) =>
                  {
                      var imBrush = new ImageBrush(bmImage);
                      this.Background = imBrush;
                      var res = MeasureArrangeHelper(_availSize);
                      this.Width = res.Width;
                      this.Height = res.Height;
                      //                      LoadInk();
                  };
        }

        public void ChkInkToggled(object sender, RoutedEventArgs e)
        {
            var isChked = e.RoutedEvent.Name == "Checked";
            if (isChked)
            {
                this.EditingMode = InkCanvasEditingMode.Ink;
                AddCtxtMenu();
            }
            else
            {
                this.ContextMenu = null;
                this.EditingMode = InkCanvasEditingMode.None;
                SaveInk();
                //      //this._pdfViewerWindow.dpPage.Children.Clear();   ///xxxxx temporary for testing 
                //      //await this._pdfViewerWindow.ShowPageAsync(_PgNo, ClearCache: false);///xxxxx temporary for testing 
            }
        }

        void AddCtxtMenu()
        {
            if (this.ContextMenu == null)
            {
                this.ContextMenu = new ContextMenu();
                this.ContextMenu.AddMnuItem("Red", "color", (o, e) =>
                {
                    var da = new DrawingAttributes
                    {
                        Color = Colors.Red
                    };
                    this.DefaultDrawingAttributes = da;
                });
                this.ContextMenu.AddMnuItem("Black", "color", (o, e) =>
                {
                    var da = new DrawingAttributes
                    {
                        Color = Colors.Black
                    };
                    this.DefaultDrawingAttributes = da;
                });

                this.ContextMenu.AddMnuItem("Highlighter", "color", (o, e) =>
                {
                    var da = new DrawingAttributes
                    {
                        IsHighlighter = true,
                        Color = Colors.Yellow,
                        Height = 30,
                        Width = 10
                    };
                    this.DefaultDrawingAttributes = da;
                });
            }

        }
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            LoadInk();
        }

        private void LoadInk()
        {
            try
            {
                if (_pdfViewerWindow.currentPdfMetaData.dictInkStrokes.TryGetValue(_PgNo, out var inkStrokeClass))
                {
                    using (var strm = new MemoryStream(inkStrokeClass.StrokeData))
                    {
                        var x = new StrokeCollection(strm);
                        var brect = x.GetBounds();
                        var xscale = this.ActualWidth / inkStrokeClass.InkStrokeDimension.X;
                        var yscale = this.ActualHeight / inkStrokeClass.InkStrokeDimension.Y;

                        var m = new Matrix(xscale, 0, 0, yscale, 0, 0);
                        x.Transform(m, applyToStylusTip: false);
                        Strokes = x;
                    }
                }
            }
            catch (Exception ex)
            {
                _pdfViewerWindow.OnException($"Load ink {this}", ex);
            }
        }

        private void SaveInk()
        {
            try
            {
                if (this.Strokes.Count > 0)
                {
                    using (var strm = new MemoryStream())
                    {
                        Strokes.Save(strm, compress: true);
                        var inkstrokeClass = new InkStrokeClass() {
                            PageNo = _PgNo,
                            InkStrokeDimension = new Point(this.Width, this.Height),
                            StrokeData = strm.GetBuffer()
                        };
                        _pdfViewerWindow.currentPdfMetaData.dictInkStrokes[_PgNo] = inkstrokeClass;
                        //_pdfViewerWindow.currentPdfMetaData.IsDirty = true;
                    }
                }
            }
            catch (Exception ex)
            {
                //RaiseEvent(new PdfViewerWindow.PdfExceptionEventAgs(PdfViewerWindow.PdfExceptionEvent, this, ex));
                _pdfViewerWindow.OnException($"Save ink {this}", ex);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _availSize = availableSize;
            var res = base.MeasureOverride(availableSize);
            return res;
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            var res = base.ArrangeOverride(arrangeSize);
            return res;
        }
        Size MeasureArrangeHelper(Size inputSize)
        {
            var naturalSize = new Size(_bmImage.Width, _bmImage.Height);
            var sizeScaleFactor = ComputeScaleFactor(inputSize,
                    naturalSize,
                    Stretch.Uniform,
                    StretchDirection.Both);
            var result = new Size(naturalSize.Width * sizeScaleFactor.Width,
                        naturalSize.Height * sizeScaleFactor.Height);
            return result;
        }

        /// <summary>
        /// This is a helper function that computes scale factors depending on a target size and a content size
        /// </summary>
        /// <param name="availableSize">Size into which the content is being fitted.</param>
        /// <param name="contentSize">Size of the content, measured natively (unconstrained).</param>
        /// <param name="stretch">Value of the Stretch property on the element.</param>
        /// <param name="stretchDirection">Value of the StretchDirection property on the element.</param>
        internal static Size ComputeScaleFactor(Size availableSize,
                                                Size contentSize,
                                                Stretch stretch,
                                                StretchDirection stretchDirection)
        {
            // Compute scaling factors to use for axes
            double scaleX = 1.0;
            double scaleY = 1.0;

            bool isConstrainedWidth = !Double.IsPositiveInfinity(availableSize.Width);
            bool isConstrainedHeight = !Double.IsPositiveInfinity(availableSize.Height);

            if ((stretch == Stretch.Uniform || stretch == Stretch.UniformToFill || stretch == Stretch.Fill)
                 && (isConstrainedWidth || isConstrainedHeight))
            {
                // Compute scaling factors for both axes
                scaleX = (DoubleUtil.IsZero(contentSize.Width)) ? 0.0 : availableSize.Width / contentSize.Width;
                scaleY = (DoubleUtil.IsZero(contentSize.Height)) ? 0.0 : availableSize.Height / contentSize.Height;

                if (!isConstrainedWidth) scaleX = scaleY;
                else if (!isConstrainedHeight) scaleY = scaleX;
                else
                {
                    // If not preserving aspect ratio, then just apply transform to fit
                    switch (stretch)
                    {
                        case Stretch.Uniform:       //Find minimum scale that we use for both axes
                            double minscale = scaleX < scaleY ? scaleX : scaleY;
                            scaleX = scaleY = minscale;
                            break;

                        case Stretch.UniformToFill: //Find maximum scale that we use for both axes
                            double maxscale = scaleX > scaleY ? scaleX : scaleY;
                            scaleX = scaleY = maxscale;
                            break;

                        case Stretch.Fill:          //We already computed the fill scale factors above, so just use them
                            break;
                    }
                }

                //Apply stretch direction by bounding scales.
                //In the uniform case, scaleX=scaleY, so this sort of clamping will maintain aspect ratio
                //In the uniform fill case, we have the same result too.
                //In the fill case, note that we change aspect ratio, but that is okay
                switch (stretchDirection)
                {
                    case StretchDirection.UpOnly:
                        if (scaleX < 1.0) scaleX = 1.0;
                        if (scaleY < 1.0) scaleY = 1.0;
                        break;

                    case StretchDirection.DownOnly:
                        if (scaleX > 1.0) scaleX = 1.0;
                        if (scaleY > 1.0) scaleY = 1.0;
                        break;

                    case StretchDirection.Both:
                        break;

                    default:
                        break;
                }
            }
            //Return this as a size now
            return new Size(scaleX, scaleY);
        }
        public override string ToString()
        {
            return $"{_pdfViewerWindow.currentPdfMetaData}   Pg={_PgNo}";
        }
    }
    internal static class DoubleUtil
    {
        // Const values come from sdk\inc\crt\float.h
        internal const double DBL_EPSILON = 2.2204460492503131e-016; /* smallest such that 1.0+DBL_EPSILON != 1.0 */
        internal const float FLT_MIN = 1.175494351e-38F; /* Number close to zero, where float.MinValue is -float.MaxValue */
        /// <summary>
        /// IsZero - Returns whether or not the double is "close" to 0.  Same as AreClose(double, 0),
        /// but this is faster.
        /// </summary>
        /// <returns>
        /// bool - the result of the AreClose comparision.
        /// </returns>
        /// <param name="value"> The double to compare to 0. </param>
        public static bool IsZero(double value)
        {
            return Math.Abs(value) < 10.0 * DBL_EPSILON;
        }
    }
}
