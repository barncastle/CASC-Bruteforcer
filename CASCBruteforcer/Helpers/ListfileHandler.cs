using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CASCBruteforcer.Helpers
{
	class ListfileHandler
	{
		public const string LISTFILE_URL = "https://bnet.marlam.in/listfile.php?unk=1";
		private readonly string[] PRODUCTS = new string[] { "wow", "wowt", "wow_beta" };

		private readonly string Product;
		private readonly string[] Exclusions;

		private string PreviousURL;


		public ListfileHandler(string product, string exclusions)
		{
			Product = product.Trim().ToLower(); // target product
			Exclusions = exclusions.Split(',').Select(x => Normalise(x)).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(); // required exclusions
		}

		public bool GetListfile(string name, string mask)
		{
			string URL = BuildURL(mask);
			if (URL == PreviousURL) // don't redownload the same list
				return false;

			try
			{
				HttpWebRequest req = (HttpWebRequest)WebRequest.Create(URL);
				req.UserAgent = "CASCBruteforcer/1.0 (+https://github.com/barncastle/CASC-Bruteforcer)"; // for tracking purposes

				using (WebResponse resp = req.GetResponse())
				using (FileStream fs = File.Create(name))
					resp.GetResponseStream().CopyTo(fs);

				req.Abort();

				PreviousURL = URL; // update cache value
				Console.WriteLine("Downloaded unknown listfile");
			}
			catch
			{
				File.Delete(name);
				Console.WriteLine($"Unable to download unknown listfile from `{LISTFILE_URL}`");
			}

			return true;
		}


		private string BuildURL(string mask)
		{
			List<string> filetypes = new List<string>()
			{
				"_lod_doodaddefsadt", "_lod_fuddlewizzadt", "_lod_fwadt", "_lodadt", "_objadt", "_tex0adt", "_xxxwmo", "adt",
				"anim", "avi", "blob", "blp", "bls", "bone", "col", "csp", "db2", "dbc", "delete", "dll", "h2o", "html", "ini",
				"lst", "lua", "m2", "manifest", "mp3", "ogg", "pd4", "phys", "pm4", "sbt", "sig", "signed", "skel", "skin", "tex",
				"toc", "ttf", "txt", "url", "wdl", "wdt", "wfx", "what", "wmo", "wtf", "wwe", "xml", "xsd", "zmp"
			};

			string url = LISTFILE_URL;

			// product filter
			url += "&product=" + (PRODUCTS.Contains(Product) ? Product : "wow_beta"); // default to wow_beta
			
			// filter by filetype and the exclusions. "unk" is always included just incase
			var extensions = new string[] { Normalise(Path.GetExtension(mask).TrimStart('.')) }.Concat(Exclusions).Distinct();
			if (!extensions.Any(x => string.IsNullOrWhiteSpace(x)))
			{
				filetypes.RemoveAll(x => extensions.Any(y => x.Contains(y))); // remove wanted filetypes
				url += $"&exclude={string.Join(",", filetypes)}";
			}

			return url;
		}

		private string Normalise(string s) => s.Trim().ToLower().Replace("%", "");
		
	}
}
