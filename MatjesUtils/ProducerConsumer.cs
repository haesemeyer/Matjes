using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;


namespace MatjesUtils
{
    public class ProducerConsumer<T> //where T:class
    {
        protected readonly object queueLock = new object();

        protected Queue<T> queue = new Queue<T>();

        //The maximum allowed element number
        public int MaxCount { get; protected set; }

        /// <summary>
        /// If true, an exception will be raised when a buffer overflow occurs
        /// </summary>
        public bool ThrowOnOverflow { get; protected set; }

        public ProducerConsumer(bool throw_on_overflow) : this(100, throw_on_overflow)
        {
        }

        public ProducerConsumer(int maxCount, bool throw_on_overflow)
        {
            MaxCount = maxCount;
        }

        /// <summary>
        /// Puts an item on the queue. Locks access to the queue and pulses the lock to signal consumers. Clears the queue if maxcount reached
        /// </summary>
        /// <param name="item">The item to add to the queue</param>
        /// <exception cref="OverflowException">If ThrowOnOverflow is set, will raise OverflowException if there is a buffer overflow</exception>
        public void Produce(T item)
        {
            lock (queueLock)
            {
                if (queue.Count > MaxCount)
                {
                    queue.Clear();
                    if (ThrowOnOverflow)
                        throw new OverflowException("Overflow, queue was cleared.");
                }
                queue.Enqueue(item);
                
                //signal waiting threads that we will release the lock
                Monitor.Pulse(queueLock);
            }
        }

        /// <summary>
        /// Retrieves an item from the queue. Waits on a lock to the queue while the queue is empty allowing items to be added.
        /// </summary>
        /// <returns>Returns the next item from the queue</returns>
        public virtual T Consume()
        {
            lock (queueLock)
            {
                while (queue.Count == 0)
                {
                    Monitor.Wait(queueLock);
                }
                return queue.Dequeue();
            }
        }

        /// <summary>
        /// Retrieves an item from the queue. Waits on a lock to the queue while the queue is empty allowing items to be added.
        /// If stop is signaled, the method will immediately return allowing the thread to stop rather than entering deadlock
        /// once the producer is retired.
        /// </summary>
        /// <param name="token">The event to signal to leave the method</param>
        /// <returns>Returns the next item from the queue</returns>
        /// <exception cref="OperationCanceledException">If requested to cancel will throw</exception>
        public virtual T Consume(CancellationToken token)
        {
            lock (queueLock)
            {
                while (queue.Count == 0)
                {
                    //If we are told to stop then we raise
                    if (token.IsCancellationRequested)
                        throw new OperationCanceledException("Consume signaled to stop");
                    //We wait for a pulse for 100ms. After that we independently try to reacquire the lock.
                    //This way if the the producer has retired we are not indefinitely stuck on the wait and can listen
                    //for a stop signal
                    Monitor.Wait(queueLock,100);
                }
                return queue.Dequeue();
            }
        }
    }
}
