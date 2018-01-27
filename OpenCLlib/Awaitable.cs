using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCLlib
{
    internal class AsyncManualResetEvent
    {
        ManualResetEvent evt;
        TimeSpan timeout;
        public AsyncManualResetEvent(bool Condition) : this(Condition, Timeout.InfiniteTimeSpan)
        {

        }

        public AsyncManualResetEvent(bool Condition, TimeSpan wait)
        {
            evt = new ManualResetEvent(Condition);
            this.timeout = wait;
        }

        public Task WaitAsync()
        {
            var tcs = new TaskCompletionSource<object>();
            var registration = ThreadPool.RegisterWaitForSingleObject(evt, (state, timedOut) =>
            {
                var localTcs = (TaskCompletionSource<object>)state;
                if (timedOut)
                    localTcs.TrySetCanceled();
                else
                    localTcs.TrySetResult(null);
            }, tcs, timeout, executeOnlyOnce: true);
            tcs.Task.ContinueWith((_, state) => ((RegisteredWaitHandle)state).Unregister(null), registration, TaskScheduler.Default);
            return tcs.Task;
        }

        public void WaitSync()
        {
            evt.WaitOne();
        }

        internal void Set()
        {
            evt.Set();
        }
    }

}
