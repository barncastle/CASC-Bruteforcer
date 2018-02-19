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
		public const string UNKNOWN_LISTFILE_URL = "https://bnet.marlam.in/listfile.php?unk=1";
		public const string KNOWN_LISTFILE_URL = "https://bnet.marlam.in/listfile.php?t={0}"; //"https://github.com/bloerwald/wow-listfile/blob/master/listfile.txt?raw=true";

		private readonly string[] PRODUCTS = new string[] { "wow", "wowt", "wow_beta", "wowz" };

		private readonly string Product;
		private readonly string[] Exclusions;

		private string PreviousURL;


		public ListfileHandler()
		{
			Product = "";
			Exclusions = new string[0];
		}

		public ListfileHandler(string product, string exclusions)
		{
			Product = product.Trim().ToLower(); // target product
			Exclusions = exclusions.Split(',').Select(x => Normalise(x)).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(); // required exclusions
		}

		public bool GetUnknownListfile(string name, string mask)
		{
			string URL = BuildURL(mask);
			string baseURL = URL.Contains("&t") ? URL.Substring(0, URL.LastIndexOf('&')) : URL;

			if (baseURL == PreviousURL) // don't redownload the same list
				return false;

			try
			{
				HttpWebRequest req = (HttpWebRequest)WebRequest.Create(URL);
				req.UserAgent = "CASCBruteforcer/1.0 (+https://github.com/barncastle/CASC-Bruteforcer)"; // for tracking purposes

				using (WebResponse resp = req.GetResponse())
				using (FileStream fs = File.Create(name))
					resp.GetResponseStream().CopyTo(fs);

				req.Abort();

				PreviousURL = baseURL; // update cache value
				Console.WriteLine("Downloaded unknown listfile");
			}
			catch
			{
				File.Delete(name);
				Console.WriteLine($"Unable to download unknown listfile from `{UNKNOWN_LISTFILE_URL}`");
			}

			return true;
		}

		public bool GetKnownListfile(out string filename, string localfile = "")
		{
			if (!string.IsNullOrWhiteSpace(localfile) && File.Exists(localfile))
			{
				filename = localfile;
				return true;
			}

			filename = "listfile.txt";

			// only redownload every few hours as this is updated as and when by the community
			if (File.Exists(filename) && (DateTime.Now - File.GetLastWriteTime(filename)).TotalHours < 4)
				return true;

			try
			{
				HttpWebRequest req = (HttpWebRequest)WebRequest.Create(string.Format(KNOWN_LISTFILE_URL, DateTime.Now.Ticks));
				req.UserAgent = "CASCBruteforcer/1.0 (+https://github.com/barncastle/CASC-Bruteforcer)"; // for tracking purposes

				using (WebResponse resp = req.GetResponse())
				using (FileStream fs = File.Create("listfile.txt"))
					resp.GetResponseStream().CopyTo(fs);

				req.Abort();

				Console.WriteLine("Downloaded known listfile");
			}
			catch
			{
				Console.WriteLine($"Unable to download known listfile from `{KNOWN_LISTFILE_URL}`");
			}

			return File.Exists(filename);
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

			string url = UNKNOWN_LISTFILE_URL;

			// product filter
			if (!string.IsNullOrWhiteSpace(Product) && PRODUCTS.Contains(Product))
				url += "&product=" + Product;

			// filter by filetype and the exclusions. "unk" is always included just incase
			var extensions = new string[] { Normalise(Path.GetExtension(mask).TrimStart('.')) }.Concat(Exclusions).Distinct();
			if (!extensions.Any(x => string.IsNullOrWhiteSpace(x)))
			{
				filetypes.RemoveAll(x => extensions.Any(y => x.Contains(y))); // remove wanted filetypes

				string types = string.Join(",", filetypes);
				url += $"&exclude={types}";
			}

			url += "&t=" + DateTime.Now.Ticks;

			return url;
		}

		private string Normalise(string s) => s.Trim().ToLower().Replace("%", "");

	}
}
