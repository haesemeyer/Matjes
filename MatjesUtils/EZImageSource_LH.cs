using ipp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MatjesUtils
{
    /// <summary>
    /// Image source that automatically marks saturated pixels in red
    /// and 0-value pixels in blue
    /// </summary>
    public unsafe sealed class EZImageSource_LH : EZImageSource
    {

        /// <summary>
        /// BGR version of the image with pixel marking applied
        /// </summary>
        Image8BGR imageDisplay;

        public override int Width
        {
            get {  return imageDisplay.Width; }
        }

        public override int Height
        {
            get { return imageDisplay.Height; }
        }

        /// <summary>
        /// This viewer does not benefit from scaling, however to allow for interchangeability
        /// we simply ignore CMax by not updating the image and not raising property changes
        /// </summary>
        public override double CMax { get => base.CMax; set{ cMax = value; } }

        public EZImageSource_LH()
        {
            imageDisplay = new Image8BGR(96, 96);
            _imageRect = new Int32Rect(0, 0, imageDisplay.Width, imageDisplay.Height);
            ///create the actual windows image source on the UI thread
            DispatcherHelper.UIDispatcher.Invoke(new Action(() => {
                ImageSource = new WriteableBitmap(imageDisplay.Width, imageDisplay.Height, 96, 96, PixelFormats.Bgr24, null);
            }));
        }


        public override void Write(Image8 image, WaitHandle cancel)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(EZImageSource_LH));
            //if image sizes don't match re-initialize raw image, scaled image and UI image
            if (imageDisplay.Width != image.Width || imageDisplay.Height != image.Height)
            {
                if (imageDisplay != null)
                    imageDisplay.Dispose();
                imageDisplay = new Image8BGR(image.Width, image.Height);
                _imageRect = new Int32Rect(0, 0, imageDisplay.Width, imageDisplay.Height);
                var done = new AutoResetEvent(false);
                DispatcherHelper.CheckBeginInvokeOnUI(() =>
                {
                    //this lock is only necessary because below after a cancel we don't wait on done - therefore upon asking the parent
                    //thread to stop, the call to Write will return, the thread will stop and disposal continues potentially disposing
                    //resources that are being used during the write (imageRaw, imageScaled, etc.). Hence an alternative would be to
                    //wait on done after cancel has been signaled.
                    lock (_disposeLock)
                    {
                        if (!IsDisposed)
                            ImageSource = new WriteableBitmap(imageDisplay.Width, imageDisplay.Height, 96, 96, PixelFormats.Bgr24, null);
                    }
                    done.Set();
                });
                //wait for our done event, indicating that the bitmap has been created (index 1) or
                //alternatively for cancel which tells us to finish
                if (WaitHandle.WaitAny(new[] { cancel, done }) == 0)
                    throw new OperationCanceledException();
            }
            //generate BGR grayscale representation
            cc.ippiGrayToRGB_8u_C1C3R(image.Image, image.Stride, imageDisplay.Image, imageDisplay.Stride, image.Size);
            //threshold and fill in color representation
            byte* thresholdLT = stackalloc byte[3];
            thresholdLT[0] = 1;
            thresholdLT[1] = 1;
            thresholdLT[2] = 1;
            byte* valueLT = stackalloc byte[3];
            valueLT[0] = 255;
            valueLT[1] = 0;
            valueLT[2] = 0;
            byte* thresholdGT = stackalloc byte[3];
            thresholdGT[0] = 254;
            thresholdGT[1] = 254;
            thresholdGT[2] = 254;
            byte* valueGT = stackalloc byte[3];
            valueGT[0] = 0;
            valueGT[1] = 0;
            valueGT[2] = 255;
            ip.ippiThreshold_LTValGTVal_8u_C3IR(imageDisplay.Image, imageDisplay.Stride, image.Size, thresholdLT, valueLT, thresholdGT, valueGT);
            //write to screen
            UpdateImage(cancel);
        }

        protected override void UpdateImage(WaitHandle cancel)
        {
            var done = new AutoResetEvent(false);
            //write raw image to screen
            DispatcherHelper.CheckBeginInvokeOnUI(() =>
            {
                //this lock is only necessary because below after a cancel we don't wait on done - therefore upon asking the parent
                //thread to stop, the call to UpdateImage will return, the thread will stop and disposal continues potentially disposing
                //resources that are being used during the write (imageRaw, imageScaled, etc.). Hence an alternative would be to
                //wait on done after cancel has been signaled.
                lock (_disposeLock)
                {
                    if (!IsDisposed)
                        ImageSource.WritePixels(_imageRect, (IntPtr)imageDisplay.Image, imageDisplay.Stride * imageDisplay.Height, imageDisplay.Stride);
                }
                done.Set();
            });
            //Block on UI thread until either we are asked to stop or write operation is finished
            if (WaitHandle.WaitAny(new[] { cancel, done }) == 0)
                throw new OperationCanceledException();
        }

        protected override void UpdateImageScaled(WaitHandle cancel)
        {
            // Scaling not available for this visualizer
            throw new NotImplementedException();
        }

        protected override void UpdateImageScaled(int timeout)
        {
            // Scaling not available for this visualizer
            throw new NotImplementedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    lock (_disposeLock)
                    {
                        if (imageDisplay != null)
                        {
                            imageDisplay.Dispose();
                        }
                    }
                }

                IsDisposed = true;
            }
        }
    }
}
