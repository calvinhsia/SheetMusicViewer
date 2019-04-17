using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfPdfViewer
{
    public class MyInkCanvas : InkCanvas
    {
        private readonly PdfViewerWindow _pdfViewerWindow;
        private readonly CheckBox _chkInk;
        readonly BitmapImage _bmImage;
        Size _availSize;

        public MyInkCanvas(BitmapImage bmImage, PdfViewerWindow pdfViewerWindow, CheckBox chkInk)
        {
            this._pdfViewerWindow = pdfViewerWindow;
            this._chkInk = chkInk;
            _bmImage = bmImage;
            if (chkInk.IsChecked == false)
            {
                this.EditingMode = InkCanvasEditingMode.None; // if we're not inking, then let mouse events change the page
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
                      //var im = new Image() { Source = bmImage };
                      //this.Children.Add(im);
                      var res = MeasureArrangeHelper(_availSize);
                      this.Width = res.Width;
                      this.Height = res.Height;
                  };
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _availSize = availableSize;
            var res = base.MeasureOverride(availableSize);
            //foreach (UIElement child in this.Children)
            //{
            //    child.Measure(availableSize);
            //}
            //var res = MeasureArrangeHelper(availableSize);
            return res;
            //            return MeasureArrangeHelper(availableSize);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            var res = base.ArrangeOverride(arrangeSize);
            //var res = MeasureArrangeHelper(arrangeSize);
            return res;
            //return MeasureArrangeHelper(arrangeSize);
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
