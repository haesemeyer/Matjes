using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesUtils
{
    public interface IAnalogProcessor<T>
    {
        public abstract void Start(ProducerConsumer<T> queue);

        public abstract void Stop();
    }
}
