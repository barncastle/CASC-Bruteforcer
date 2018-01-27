using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCLlib
{

    //Fixed method on CPU or GPU
    public class ComputeMethod
    {
        private EasyCL cl;
        public string Source { get; private set; }
        public string Method { get; private set; }
        public AcceleratorDevice Device { get; private set; }

        /// <summary>
        /// Gets last invocation time of synchronous Invoke Method
        /// </summary>
        public TimeSpan LastInvocationTime { get; private set; } = TimeSpan.Zero;

        Stopwatch watch = new Stopwatch();


        /// <summary>
        /// You only need to Invoke Once and subsequent calls will work on the same arrays
        /// </summary>
        public void Invoke(long Offset, long WorkSize)
        {
            watch.Restart();
            cl.Invoke(Method, Offset, WorkSize);
            LastInvocationTime = watch.Elapsed;
        }

        /// <summary>
        /// You only need to Invoke Once and subsequent calls will work on the same arrays
        /// </summary>
        public void Invoke(long WorkSize)
        {
            Invoke(0, WorkSize);
        }

        public void Invoke(long WorkSize, params object[] Arguments)
        {
            Invoke(0, WorkSize, Arguments);
        }

        public void Invoke(long Offset, long WorkSize, params object[] Arguments)
        {
            watch.Restart();
            cl.Invoke(Method, 0,  WorkSize, Arguments);
            LastInvocationTime = watch.Elapsed;
        }

        public Task InvokeAsync(long WorkSize, params object[] Arguments)
        {
            return cl.InvokeAsync(Method, WorkSize, Arguments);
        }

        public ComputeMethod(string Kernel, string Method, AcceleratorDevice device)
        {
            this.Source = Kernel;
            this.Method = Method;
            this.Device = device;

            cl = new EasyCL() { Accelerator = device };
            cl.LoadKernel(Kernel);
        }

        public override string ToString()
        {
            return Method;
        }
    }
}
