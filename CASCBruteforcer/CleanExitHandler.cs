using CASCBruteforcer.Bruteforcers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CASCBruteforcer
{
	static class CleanExitHandler
	{
		public static bool IsProcessing = false;
		
		[DllImport("Kernel32")]
		private static extern bool SetConsoleCtrlHandler(ExitEventHandler handler, bool add);

		private delegate void ExitEventHandler(CtrlType ctrlType);
		private static ExitEventHandler ExitHandler;
		private static bool requiresExit = false;


		public static void Attach()
		{
			ExitHandler += ExitEventAction;
			SetConsoleCtrlHandler(ExitHandler, true);
		}

		public static void ProcessExit()
		{
			IsProcessing = false;

			if (requiresExit)
				Environment.Exit(-1);
		}


		private static void ExitEventAction(CtrlType ctrlType)
		{
			if (!IsProcessing || requiresExit)
			{
				Environment.Exit(-1); // not locked OR close event fired twice, force exit
			}
			else
			{
				requiresExit = true; // locked, queue exit
				Console.WriteLine("Exiting...");
			}
		}


		enum CtrlType
		{
			CTRL_C_EVENT = 0,
			CTRL_BREAK_EVENT = 1,
			CTRL_CLOSE_EVENT = 2,
			CTRL_LOGOFF_EVENT = 5,
			CTRL_SHUTDOWN_EVENT = 6
		}
	}


}
