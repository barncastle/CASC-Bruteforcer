using Cloo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenCLlib
{
	public class MultiCL
	{
		public AcceleratorDevice[] Accelerators;
		public OpenCL[] Context;
		public event EventHandler<double> ProgressChangedEvent;

		public MultiCL(ComputeDeviceTypes filter = ComputeDeviceTypes.All)
		{
			SetFilter(filter);
		}

		public void SetFilter(ComputeDeviceTypes filter)
		{
			this.Accelerators = AcceleratorDevice.All.Where(x => filter.HasFlag(x.Type)).ToArray();
			this.Context = Accelerators.Select(x => new OpenCL() { Accelerator = x }).ToArray();
		}


		public void SetKernel(string Kernel, string Method)
		{
			foreach (var c in Context)
				c.SetKernel(Kernel, Method);
		}

		public void SetParameter(params object[] Arguments)
		{
			foreach (var c in Context)
				c.SetParameter(Arguments);
		}


		public void Invoke(long FromInclusive, long ToInclusive, int Parts)
		{
			if (Parts <= 1)
			{
				Task.WaitAll(Enqueue(FromInclusive, ToInclusive, Context.First()));
				ProgressChangedEvent?.Invoke(this, 1.0);
				return;
			}

			Queue<Tuple<long, long>> parts = new Queue<Tuple<long, long>>(); //split Indicess into parts
			long delta = (ToInclusive - FromInclusive) / Parts;
			for (long i = 0; i < Parts; i++)
			{
				Tuple<long, long> local = new Tuple<long, long>(i * delta, (i + 1) * delta);
				parts.Enqueue(local);
			}

			if (Parts * delta != ToInclusive)
				parts.Enqueue(new Tuple<long, long>(Parts * delta, ToInclusive));

			int startlen = parts.Count;
			var worktodo = parts.Dequeue();

			List<Task> Tasks = new List<Task>(); //Initialize Tasks Array
			for (int i = 0; i < Context.Length && i <= ToInclusive; i++)
			{
				Tasks.Add(Enqueue(worktodo.Item1, worktodo.Item2, Context[i]));
			}

			while (parts.Count >= 1) //finishes when all work is started
			{
				var nextwork = parts.Dequeue();
				int finishedindex = Task.WaitAny(Tasks.ToArray()); //device on which invoke was called
				Tasks[finishedindex] = Enqueue(nextwork.Item1, nextwork.Item2, Context[finishedindex]); //start next workitem

				ProgressChangedEvent?.Invoke(this, (startlen - parts.Count) / (double)startlen);
			}

			Task.WaitAll(Tasks.ToArray()); //waits for all finish
		}

		private Task Enqueue(long from, long to, OpenCL acc)
		{
			return Task.Run(() => acc.Execute(from, to - from, -1));
		}


		public T[] InvokeReturn<T>(long Worksize, long LocalWorksize, int Outsize) where T : struct
		{
			if (Context.Length == 0)
				throw new Exception("No compatible Context found");

			// single context so run
			if (Context.Length == 1)
				return Context[0].ExecuteReturn<T>(Worksize, LocalWorksize, 0, Outsize);

			// multiple Context so split workload
			Task<T[]>[] Tasks = new Task<T[]>[Context.Length];
			long baseworksize = Math.DivRem(Worksize, Context.Length, out long remainder); // split evenly

			for (int i = 0; i < Context.Length; i++)
			{
				long worksize = baseworksize;
				if (i == Context.Length - 1)
					worksize += remainder; // add remaining items to the last batch

				int c = i; // local ref for the Task
				Tasks[c] = Task.Run(() => Context[c].ExecuteReturn<T>(worksize, LocalWorksize, baseworksize * c, Outsize));
			}

			Task.WaitAll(Tasks);
			return Tasks.SelectMany(x => x.Result).ToArray();
		}

	}
}
