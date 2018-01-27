using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.IO;
using Cloo;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenCLlib
{
	public enum MemoryFlags : long
	{
		//
		// Summary:
		//     Let the OpenCL choose the default flags.
		None = 0,
		//
		// Summary:
		//     The Cloo.ComputeMemory will be accessible from the Cloo.ComputeKernel for read
		//     and write operations.
		ReadWrite = 1,
		//
		// Summary:
		//     The Cloo.ComputeMemory will be accessible from the Cloo.ComputeKernel for write
		//     operations only.
		WriteOnly = 2,
		//
		// Summary:
		//     The Cloo.ComputeMemory will be accessible from the Cloo.ComputeKernel for read
		//     operations only.
		ReadOnly = 4,
		//
		UseHostPointer = 8,
		//
		AllocateHostPointer = 16,
		//
		CopyHostPointer = 32
	}

	public class CLMethod
	{
		public string Name;
		public CLArgumentInfo[] Arguments;

		public CLMethod(string EntryPoint, string Body)
		{
			this.Name = EntryPoint;

			int found = Body.IndexOf(EntryPoint);
			int start = Body.IndexOf('(', found) + 1;
			int end = Body.IndexOf(')', found);
			
			string parameter = Body.Substring(start, end - start);
			var parameters = parameter.Split(',');

			Arguments = parameters.Select(x => new CLArgumentInfo(x)).ToArray();
		}
	}

	[Serializable]
	public class Computeperformance
	{
		public string Description { get; private set; }
		public double GFlops_GBit { get; private set; }

		public Computeperformance(double GFlops_GBit, string Description)
		{
			this.Description = Description;
			this.GFlops_GBit = GFlops_GBit;
		}
	}

	public class CLArgument<T> where T : struct
	{
		public bool CopyBack;
		public T[] Data;
		public T ComputeValue;
		public MemoryFlags Flags;

		public CLArgument() { }

		public CLArgument(bool copyback, T[] data, MemoryFlags flags)
		{
			CopyBack = copyback;
			Data = data;
			Flags = flags;
		}

		public static CLArgument<T> CreateReturn(T[] Data)
		{
			CLArgument<T> arg = new CLArgument<T>
			{
				Data = Data,
				CopyBack = true,
				Flags = MemoryFlags.WriteOnly | MemoryFlags.AllocateHostPointer
			};
			return arg;
		}

		public static CLArgument<T> CreateReturn(int Size)
		{
			CLArgument<T> arg = new CLArgument<T>
			{
				Data = new T[Size],
				CopyBack = true,
				Flags = MemoryFlags.WriteOnly | MemoryFlags.AllocateHostPointer
			};
			return arg;
		}

		public static CLArgument<T> CreateArray(T[] Data)
		{
			CLArgument<T> arg = new CLArgument<T>
			{
				Data = Data,
				Flags = MemoryFlags.ReadWrite | MemoryFlags.UseHostPointer
			};
			return arg;
		}

		public static CLArgument<T> CreateValue(T Value)
		{
			CLArgument<T> arg = new CLArgument<T>
			{
				ComputeValue = Value
			};
			return arg;
		}


		public static implicit operator CLArgument<T>(T[] data)
		{
			return CreateArray(data);
		}

		public static implicit operator CLArgument<T>(T value)
		{
			return CreateValue(value);
		}

		internal ComputeMemory GenerateComputeMemory(ComputeContext context)
		{
			if (Data == null) return null;

			if (CopyBack)
			{
				return new ComputeBuffer<T>(context, (ComputeMemoryFlags)Flags, Data.Length);
			}
			else
			{
				return new ComputeBuffer<T>(context, (ComputeMemoryFlags)Flags, Data);
			}

		}

	}

	public class CLArgumentInfo
	{
		public bool CopyBack { get; set; }
		public ComputeMemory ComputeMemory { get; set; }
		public dynamic ComputeValue { get; set; }

		string Name;
		string AccessModifier;
		string SpaceModifier;
		string Type;

		public bool IsArray;

		public CLArgumentInfo(string arg)
		{
			arg = System.Text.RegularExpressions.Regex.Replace(arg, @"\s+", " ", RegexOptions.Compiled); //  __global   int => __global int
			arg = arg.Replace("__", "");// __global *int-> global *int;
			arg = arg.TrimStart();// global *int->global *int
			arg = arg.Replace(" *", "* ");//global *int->global *int;


			var info = arg.Split(' ');

			Name = info.Last();

			for (int i = 0; i < info.Length - 1; i++)
			{
				string mod = info[i];
				if (mod == "global" || mod == "local" || mod == "constant" || mod == "private")
				{
					AccessModifier = mod; continue;
				}
				if (mod == "read_only" || mod == "write_only" || mod == "read_write")
				{
					SpaceModifier = mod; continue;
				}
				if (mod.Contains("*"))
				{
					IsArray = true;
					Type = mod.Replace("*", "") + "[]"; continue;
				}
				Type = mod;
			}
		}


		public override string ToString()
		{
			return $"{AccessModifier} {SpaceModifier} {Type} {Name}";
		}
	}

	/// <summary>
	/// For easier use try EasyCL
	/// </summary>
	public class OpenCL
	{
		ComputeContext context;
		ComputeCommandQueue queue;
		ComputeKernel kernel;
		bool MethodSet = false;
		string OpenCLBody;
		string EntryPoint;

		public string PlatformName => context.Platform.Name;

		public string AcceleratorName => context.Devices[0].Name;

		AcceleratorDevice _device = AcceleratorDevice.GPU;
		public AcceleratorDevice Accelerator
		{
			get
			{
				return _device;
			}
			set
			{
				if (value != _device)
				{
					_device = value;
					CreateContext();
					if (MethodSet)
					{
						CLMethod tmp = this.MethodInfo;
						SetKernel(OpenCLBody, EntryPoint);
						this.MethodInfo = tmp;
					}
				}
			}
		}
		public CLMethod MethodInfo;

		public OpenCL()
		{
			CreateContext();
		}

		public void SetKernel(string OpenCLBody, string EntryPoint)
		{
			this.OpenCLBody = OpenCLBody;
			this.EntryPoint = EntryPoint;
			ComputeProgram program = new ComputeProgram(context, OpenCLBody);
			try
			{
				program.Build(null, null, null, IntPtr.Zero);
				kernel = program.CreateKernel(EntryPoint);
			}
			catch (BuildProgramFailureComputeException)
			{
				string message = program.GetBuildLog(Accelerator.Device);
				throw new ArgumentException(message);
			}

			MethodInfo = new CLMethod(EntryPoint, OpenCLBody);
			MethodSet = true;
		}

		public void SetEntryPoint(string EntryPoint)
		{
			this.EntryPoint = EntryPoint;
			MethodInfo = new CLMethod(EntryPoint, OpenCLBody);
			MethodSet = true;
		}
		
		void CreateContext()
		{
			context = new ComputeContext(_device.Type, new ComputeContextPropertyList(Accelerator.Device.Platform), null, IntPtr.Zero);
			queue = new ComputeCommandQueue(context, context.Devices[0], ComputeCommandQueueFlags.None);
		}

		public CLArgumentInfo SetValueArgument<T>(int index, T Value) where T : struct
		{
			MethodInfo.Arguments[index].ComputeValue = Value;
			return MethodInfo.Arguments[index];
		}

		public CLArgumentInfo SetArgument<T>(int index, T[] Value, MemoryFlags flags) where T : struct
		{
			MethodInfo.Arguments[index].ComputeMemory = new ComputeBuffer<T>(context, (ComputeMemoryFlags)flags, Value);
			return MethodInfo.Arguments[index];
		}

		public CLArgumentInfo SetReturnArgument<T>(int index, long size, MemoryFlags flags) where T : struct
		{
			MethodInfo.Arguments[index].CopyBack = true;
			MethodInfo.Arguments[index].ComputeMemory = new ComputeBuffer<T>(context, (ComputeMemoryFlags)flags, size);
			return MethodInfo.Arguments[index];
		}

		public CLArgumentInfo SetArgumentDirect<T>(int index, CLArgument<T> arg) where T : struct
		{
			MethodInfo.Arguments[index].CopyBack = arg.CopyBack;
			MethodInfo.Arguments[index].ComputeMemory = arg.GenerateComputeMemory(context);
			MethodInfo.Arguments[index].ComputeValue = arg.ComputeValue;
			return MethodInfo.Arguments[index];
		}

		void SetArgs()
		{
			for (int i = 0; i < MethodInfo.Arguments.Length; i++)
			{
				var arg = MethodInfo.Arguments[i];

				if (arg.ComputeMemory == null && arg.IsArray) throw new ArgumentException("Expected Array at index " + i);
				if (arg.ComputeMemory != null && arg.IsArray == false) throw new ArgumentException("Expected Variable at index " + i);

				if (arg.ComputeMemory != null)
				{
					kernel.SetMemoryArgument(i, arg.ComputeMemory);
				}
				else
				{
					kernel.SetValueArgument(i, arg.ComputeValue);
				}
			}
		}

		void _SetGenericArg(int index, object genericarg)
		{
			this.GetType().GetMethod(nameof(SetArgumentDirect)).MakeGenericMethod(genericarg.GetType().GetGenericArguments()[0]).Invoke(this, new object[] { index, genericarg });
		}
		
		public void SetParameter(params object[] arguments)
		{
			for (int i = 0; i < arguments.Length; i++)
			{
				object argument = arguments[i];
				Type argumenttype = arguments[i].GetType();
				bool IsArray = argumenttype.IsArray;
				bool IsGeneric = argumenttype.IsGenericType;
				bool ISCLArgument = IsArray == false && IsGeneric == true && argumenttype.GetGenericTypeDefinition() == typeof(CLArgument<>);

				if (ISCLArgument)
				{
					_SetGenericArg(i, argument);
					continue;
				}

				if (IsArray)
				{
					string methodname = nameof(CLArgument<int>.CreateArray);
					MethodInfo CLargconstruct = typeof(CLArgument<>).MakeGenericType(argumenttype.GetElementType()).GetMethod(methodname, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

					object CLarg = CLargconstruct.Invoke(null, new object[] { argument });
					_SetGenericArg(i, CLarg);
					continue;
				}

				string varname = nameof(CLArgument<int>.CreateValue);
				MethodInfo CLvarconstruct = typeof(CLArgument<>).MakeGenericType(argumenttype).GetMethod(varname, BindingFlags.Static | BindingFlags.Public);
				object CLvarg = CLvarconstruct.Invoke(null, new object[] { argument });
				_SetGenericArg(i, CLvarg);
			}
		}

		public void Execute(int Worksize)
		{
			Execute(0, Worksize, -1, null);
		}

		public void Execute(int Worksize, int Localsize)
		{
			Execute(0, Worksize, Localsize);
		}

		/// <summary>
		/// Ignores previously set parameters set by OpenCL.SetArgument, is slower than Execute
		/// </summary>
		public void Execute(long Offset, long Worksize, long Localsize, params object[] parameter)
		{
			if (parameter == null || parameter.Length == 0)
			{
				SetArgs();
			}
			else
			{
				SetParameter(parameter);
				SetArgs();
			}

			if (Localsize == -1)
			{
				queue.Execute(kernel, new long[] { Offset }, new long[] { Worksize }, null, null);
			}
			else
			{
				queue.Execute(kernel, new long[] { Offset }, new long[] { Worksize }, new long[] { Localsize }, null);
			}

			queue.Finish();
		}

		public T[] ExecuteReturn<T>(long Worksize, long WorkOffset, int OutSize) where T : struct
		{
			T[] Returned = new T[OutSize];

			SetArgs();

			if(WorkOffset != 0)
				queue.Execute(kernel, new long[] { WorkOffset }, new long[] { Worksize }, null, null);
			else
				queue.Execute(kernel, null, new long[] { Worksize }, null, null);		

			for (int i = 0; i < MethodInfo.Arguments.Length; i++)
			{
				if (MethodInfo.Arguments[i].CopyBack)
					queue.ReadFromBuffer((ComputeBuffer<T>)MethodInfo.Arguments[i].ComputeMemory, ref Returned, false, null);
			}

			queue.Finish();

			return Returned;
		}
	}
}