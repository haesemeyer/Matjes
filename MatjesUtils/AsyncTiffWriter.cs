using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using ipp;

namespace MatjesUtils
{
    public unsafe class AsyncTiffWriter : IDisposable
    {
        private TiffWriter _tiffWriter;

        private ProducerConsumer<Image8> _imageQueue;

        private CancellationTokenSource _cancellationTokenSource;

        public AsyncTiffWriter(string fileName)
        {
            _tiffWriter = new TiffWriter(fileName);
            _imageQueue = new ProducerConsumer<Image8>(4000, false);
            _cancellationTokenSource = new CancellationTokenSource();
            System.Threading.Tasks.Task.Run(() => TaskRun(_cancellationTokenSource.Token));
        }

        private void TaskRun(CancellationToken token)
        {
            Image8 nextImage;
            while (true)
            {
                try
                {
                    nextImage = _imageQueue.Consume(token);
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("Image writing canceled");
                    return;
                }
                if (nextImage != null)
                {
                    _tiffWriter.WriteFrame(nextImage);
                    nextImage.Dispose();
                    nextImage = null;
                }
            }
        }

        public void WriteFrame(Image8 image)
        {
            //Create new image buffer and copy over image, then produce on ProducerConsumer
            Image8 copy = new Image8(image.Size);
            ip.ippiCopy_8u_C1R(image.Image, image.Stride, copy.Image, copy.Stride, image.Size);
            _imageQueue.Produce(copy);
            //Note: Consumer task will free buffer!!!
        }

        public bool IsDisposed { get; private set; }
        public void Dispose()
        {
            if (IsDisposed)
                return;
            //Signal to our task to stop
            _cancellationTokenSource.Cancel();
            _tiffWriter.Dispose();
            _cancellationTokenSource.Dispose();
            IsDisposed = true;
        }
    }
}
