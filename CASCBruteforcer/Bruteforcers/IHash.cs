using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CASCBruteforcer.Bruteforcers
{
	interface IHash
	{
		void LoadParameters(params string[] args);
		void Start();
	}
}
