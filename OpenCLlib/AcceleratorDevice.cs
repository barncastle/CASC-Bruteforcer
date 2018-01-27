using Cloo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCLlib
{
    public class AcceleratorDevice
    {
        public static AcceleratorDevice[] All = ComputePlatform.Platforms.SelectMany(x => x.Devices).Select(x => new AcceleratorDevice(x)).ToArray();

        public static bool HasCPU => All.Any(x => x.Type == ComputeDeviceTypes.Cpu);

        public static bool HasGPU => All.Any(x => x.Type == ComputeDeviceTypes.Gpu);

        public static AcceleratorDevice CPU
        {
            get
            {
                AcceleratorDevice cpu = All.FirstOrDefault(x => x.Type == ComputeDeviceTypes.Cpu);
                if (cpu == null)
                    throw new InvalidOperationException("No OpenCL compatible CPU found on this computer.");

                return cpu;
            }
        }

        public static AcceleratorDevice GPU
        {
            get
            {
                AcceleratorDevice gpu = All.FirstOrDefault(x => x.Type == ComputeDeviceTypes.Gpu);
                if (gpu == null)
                    throw new InvalidOperationException("No OpenCL compatible GPU found on this computer.");

				return gpu;
            }
        }


		public ComputeDevice Device { get; private set; }
		public string Name => Device.Name;
		public string Vendor => Device.Vendor;
		public ComputeDeviceTypes Type => Device.Type;

        public AcceleratorDevice(ComputeDevice Device)
        {
            this.Device = Device;
        }

        public override string ToString()
		{
			string type = "Unknown";
			if (Type.HasFlag(ComputeDeviceTypes.Gpu))
				type = "GPU";
			else if (Type.HasFlag(ComputeDeviceTypes.Cpu))
				type = "CPU";

			return Name.Trim() + $" ({type})";
		}
    }
}
