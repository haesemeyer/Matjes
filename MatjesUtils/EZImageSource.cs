using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MatjesUtils
{
    public abstract class EZImageSource : PropertyChangeNotification, IDisposable
    {

        /// <summary>
        /// To ensure we don't dispose images during a write
        /// </summary>
        protected object _disposeLock = new object();

        public bool IsDisposed
        {
            get; protected set;
        }

        /// <summary>
        /// The windows image source
        /// </summary>
        WriteableBitmap? imageSource;

        /// <summary>
        /// The windows image source
        /// </summary>
        public WriteableBitmap ImageSource
        {
            get { return imageSource; }
            protected set { imageSource = value; RaisePropertyChanged(nameof(ImageSource)); }
        }

        protected Int32Rect _imageRect;

        /// <summary>
        /// Brightness scale - pixel values
        /// >=cMax will be set to 255
        /// </summary>
        protected double cMax;

        /// <summary>
        /// Brightness scale - pixel values
        /// >=cMax will be set to 255
        /// </summary>
        public virtual double CMax
        {
            get { return cMax; }
            set
            {
                if (cMax < 0 || cMax > 255)
                    throw new ArgumentOutOfRangeException("CMax", "CMax has to be >=0 and <=255");
                cMax = value;
                //redraw newly scaled image
                UpdateImageScaled(1000);
                RaisePropertyChanged(nameof(CMax));
            }
        }

        public abstract int Width { get; }

        public abstract int Height { get; }

        public abstract void Write(Image8 image, WaitHandle cancel);

        protected abstract void UpdateImage(WaitHandle cancel);

        protected abstract void UpdateImageScaled(WaitHandle cancel);
        
        protected abstract void UpdateImageScaled(int timeout);

        protected abstract void Dispose(bool disposing);

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
