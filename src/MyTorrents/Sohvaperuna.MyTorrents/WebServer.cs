using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Sohvaperuna.TorrentEngine;
using Sohvaperuna.TorrentSearch;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.45): MyTorrents in a web browser on the home network (laptop, desktop, phone) - run by the background
	/// helper, e.g. http://yourpc:9118. Pages: My shows, Wanted movies, Not found, Downloads, Search, Search sites,
	/// Settings. Home network only (private addresses), optional password. Everything goes through the same files
	/// and requests MediaPortal and MyTorrents Configuration use, so it works whether MediaPortal is open or not.
	/// </summary>
	public static class WebServer
	{
		public static bool On => Configuration.GetExtra("Web.On", "false") == "true";

		public static int Port
		{
			get
			{
				int p;
				return int.TryParse(Configuration.GetExtra("Web.Port", "9118"), out p) && p > 0 && p < 65536 ? p : 9118;
			}
		}

		public static string Password => Configuration.GetExtra("Web.Password", "") ?? "";

		private static TcpListener _listener;
		private static Thread _thread;

		public static void Start()
		{
			if (_listener != null || !On) return;
			try
			{
				_listener = new TcpListener(IPAddress.Any, Port);
				_listener.Start();
				_thread = new Thread(Loop) { IsBackground = true, Name = "MyTorrents web page", Priority = ThreadPriority.BelowNormal };
				_thread.Start();
				Log.Instance().Print("Web page: open http://" + Dns.GetHostName().ToLowerInvariant() + ":" + Port + " on the home network");
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Web page: could not start on port " + Port + ": " + ex.Message);
				_listener = null;
			}
		}

		public static void Stop()
		{
			try
			{
				_listener?.Stop();
			}
			catch
			{
			}
			_listener = null;
		}

		private static void Loop()
		{
			while (_listener != null)
			{
				TcpClient c;
				try
				{
					c = _listener.AcceptTcpClient();
				}
				catch
				{
					return;
				}
				ThreadPool.QueueUserWorkItem(_ => Handle(c));
			}
		}

		private static bool HomeNetwork(IPAddress a)
		{
			if (IPAddress.IsLoopback(a)) return true;
			if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
			byte[] b = a.GetAddressBytes();
			if (b.Length == 4) return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
			return a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || (b[0] & 0xfe) == 0xfc;
		}

		private static void Handle(TcpClient c)
		{
			using (c)
			{
				try
				{
					c.ReceiveTimeout = 10000;
					NetworkStream ns = c.GetStream();
					IPAddress from = ((IPEndPoint)c.Client.RemoteEndPoint).Address;
					// read the request (headers, then the form body)
					MemoryStream raw = new MemoryStream();
					byte[] buf = new byte[8192];
					int headerEnd = -1, contentLength = 0;
					while (raw.Length < 1024 * 1024)
					{
						int n = ns.Read(buf, 0, buf.Length);
						if (n <= 0) break;
						raw.Write(buf, 0, n);
						string sofar = Encoding.UTF8.GetString(raw.ToArray());
						if (headerEnd < 0)
						{
							headerEnd = sofar.IndexOf("\r\n\r\n", StringComparison.Ordinal);
							if (headerEnd >= 0)
							{
								System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(sofar.Substring(0, headerEnd), @"Content-Length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
								if (m.Success) contentLength = int.Parse(m.Groups[1].Value);
							}
						}
						if (headerEnd >= 0 && Encoding.UTF8.GetByteCount(sofar.Substring(headerEnd + 4)) >= contentLength) break;
					}
					string text = Encoding.UTF8.GetString(raw.ToArray());
					if (headerEnd < 0) return;
					string head = text.Substring(0, headerEnd), body = text.Substring(headerEnd + 4);
					string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
					string[] first = lines[0].Split(' ');
					if (first.Length < 2) return;
					string method = first[0], target = first[1];
					if (!HomeNetwork(from))
					{
						Send(ns, "403 Forbidden", "text/plain", "MyTorrents only answers on your home network.");
						return;
					}
					if (Password.Length > 0)
					{
						string auth = lines.FirstOrDefault(l => l.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) ?? "";
						string expect = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("mytorrents:" + Password));
						if (auth.Substring(Math.Min(auth.Length, 14)).Trim() != expect)
						{
							Send(ns, "401 Unauthorized", "text/plain", "Password needed (user name: mytorrents).", "WWW-Authenticate: Basic realm=\"MyTorrents\"\r\n");
							return;
						}
					}
					string path = target, query = "";
					int q = target.IndexOf('?');
					if (q >= 0)
					{
						path = target.Substring(0, q);
						query = target.Substring(q + 1);
					}
					Dictionary<string, string> form = Parse(query);
					if (method == "POST") foreach (KeyValuePair<string, string> kv in Parse(body)) form[kv.Key] = kv.Value;
					string redirect = method == "POST" ? Act(path, form) : null;
					if (redirect != null)
					{
						Send(ns, "303 See Other", "text/plain", "", "Location: " + redirect + "\r\n");
						return;
					}
					Send(ns, "200 OK", "text/html; charset=utf-8", Page(path, form));
				}
				catch (Exception ex)
				{
					Log.Instance().Print("Web page: " + ex.Message);
				}
			}
		}

		private static void Send(NetworkStream ns, string status, string type, string content, string extraHeaders = "")
		{
			byte[] b = Encoding.UTF8.GetBytes(content ?? "");
			byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + b.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n" + extraHeaders + "\r\n");
			ns.Write(h, 0, h.Length);
			ns.Write(b, 0, b.Length);
		}

		private static Dictionary<string, string> Parse(string s)
		{
			Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string part in (s ?? "").Split('&'))
			{
				if (part.Length == 0) continue;
				int e = part.IndexOf('=');
				string k = Uri.UnescapeDataString((e < 0 ? part : part.Substring(0, e)).Replace('+', ' '));
				string v = e < 0 ? "" : Uri.UnescapeDataString(part.Substring(e + 1).Replace('+', ' '));
				d[k] = v;
			}
			return d;
		}

		private static string H(string s) => WebUtility.HtmlEncode(s ?? "");

		private static string Get(Dictionary<string, string> f, string k) => f.TryGetValue(k, out string v) ? v : "";

		// ------------------------------------------------------------------ actions (POST) - return where to go next

		private static string Act(string path, Dictionary<string, string> f)
		{
			string a = Get(f, "a");
			try
			{
				switch (a)
				{
				case "check": GrabRequests.AddCheck(Get(f, "id"), Get(f, "name")); return "/shows?msg=" + Uri.EscapeDataString("Looking for " + Get(f, "name") + " now");
				case "checkall": GrabRequests.AddCheck("*", "all shows"); return "/shows?msg=" + Uri.EscapeDataString("Checking all your shows now");
				case "grabmissing": GrabRequests.AddGrabMissing(Get(f, "id"), Get(f, "name")); return "/shows?msg=" + Uri.EscapeDataString("Getting the missing ones of " + Get(f, "name"));
				case "addmovie":
					bool added = WantedFilms.Add(Get(f, "title"), Get(f, "year"));
					return "/movies?msg=" + Uri.EscapeDataString(added ? Get(f, "title") + " added" : Get(f, "title") + " is already on the list");
				case "lookmovie": WantedFilms.LookAgain(Get(f, "title"), Get(f, "year")); return "/movies?msg=" + Uri.EscapeDataString("Looking for " + Get(f, "title") + " soon");
				case "removemovie": WantedFilms.Remove(Get(f, "title"), Get(f, "year")); return "/movies?msg=" + Uri.EscapeDataString(Get(f, "title") + " removed");
				case "reread": GrabRequests.AddRereadMovies(); return "/movies?msg=" + Uri.EscapeDataString("Reading your movies folders again");
				// 2026 (1.3.46): your movies - collections and 5.1 upgrades
				case "makecoll":
				{
					Collections.Suggestion c = Collections.Suggestions().FirstOrDefault(x => x.Name == Get(f, "name"));
					if (c == null) return "/library?msg=" + Uri.EscapeDataString("Not suggested any more");
					System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { string rr = Collections.Make(c); Log.Instance().Print("Collections: " + c.Name + " - " + (rr.Length == 0 ? "done" : rr)); } catch (Exception ex) { Log.Instance().Print("Collections: " + ex.Message); } });
					return "/library?msg=" + Uri.EscapeDataString("Moving " + c.Films.Count + " movies into " + c.Name + " - give it a minute");
				}
				case "missingcoll":
				{
					Collections.Suggestion c = Collections.Suggestions().FirstOrDefault(x => x.Name == Get(f, "name"));
					List<Collections.Film> gone = c == null ? new List<Collections.Film>() : Collections.Missing(c);
					foreach (Collections.Film g in gone) WantedFilms.Add(g.Title, g.Year > 0 ? g.Year.ToString() : "");
					return "/library?msg=" + Uri.EscapeDataString(gone.Count == 0 ? "Nothing missing from " + Get(f, "name") : "Added to Wanted movies: " + string.Join(", ", gone.Select(g => g.Display)));
				}
				case "ignorecoll":
				{
					Collections.Suggestion c = Collections.Suggestions().FirstOrDefault(x => x.Name == Get(f, "name"));
					if (c != null) Collections.Ignore(c);
					return "/library?msg=" + Uri.EscapeDataString("OK - " + Get(f, "name") + " won't be suggested again");
				}
				case "checknow":
					System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { CheckUp.Run(); } catch { } });
					return "/checkup?msg=" + Uri.EscapeDataString("Checking - refresh this page in a minute or two");
				case "cufix":
				case "cuignore":
				{
					CheckUp.Finding cf = CheckUp.Read().FirstOrDefault(x => x.Key == Get(f, "key"));
					if (cf == null) return "/checkup?msg=" + Uri.EscapeDataString("Not on the list any more");
					string done = "Done";
					if (a == "cuignore") { CheckUp.Ignore(cf); done = "OK - won't mention it again"; }
					else if (cf.Action == "import") { CheckUp.ImportAgain(cf); done = "MP-TVSeries is adding it"; }
					else if (cf.Action == "dupe") { string r2 = CheckUp.DeleteExtra(cf); done = r2.Length == 0 ? "Deleted the extra copy" : r2; }
					else if (cf.Action == "better") { PoorCopies.Look(cf.Title, cf.Year, cf.File); CheckUp.Done(cf); done = cf.Title + " is on Wanted movies to replace"; }
					else if (cf.Action == "leftover") { string r3 = CheckUp.DeleteLeftover(cf); done = r3.Length == 0 ? "Deleted the sample / advert video" : r3; }
					return "/checkup?msg=" + Uri.EscapeDataString(done);
				}
				case "putback": // 2026 (1.3.65): a hidden movie back on Movies to improve
					Improve.PutBack(Get(f, "file"));
					return "/library?msg=" + Uri.EscapeDataString("Put back - it's on Movies to improve again if it still needs it");
				case "putbackall":
					foreach (Improve.Item hi in Improve.Hidden()) Improve.PutBack(hi.File);
					return "/library?msg=" + Uri.EscapeDataString("All put back");
				case "improve":
				case "ignoreimprove":
				{
					// 2026 (1.3.65): Movies to improve - one list
					Improve.Item m = Improve.List().FirstOrDefault(x => x.File == Get(f, "file"));
					if (m == null) return "/library?msg=" + Uri.EscapeDataString("Not on the list any more");
					if (a == "improve") Improve.Look(m);
					else Improve.Ignore(m);
					return "/library?msg=" + Uri.EscapeDataString(a == "improve" ? Improve.Added(m) : "OK - " + m.Display + " stays as it is");
				}
				case "filemovie":
					MovieFiling.Pending p = MovieFiling.Waiting().FirstOrDefault(x => x.From == Get(f, "from"));
					string r = p == null ? "Not waiting any more" : MovieFiling.Approve(p);
					return "/movies?msg=" + Uri.EscapeDataString(r.Length == 0 ? "Filed" : r);
				case "leavemovie":
					MovieFiling.Pending p2 = MovieFiling.Waiting().FirstOrDefault(x => x.From == Get(f, "from"));
					if (p2 != null) MovieFiling.Forget(p2);
					return "/movies";
				case "looknow": CantFind.LookNow(Get(f, "key")); GrabRequests.AddCheck(Get(f, "id"), Get(f, "name")); return "/notfound?msg=" + Uri.EscapeDataString("Looking now");
				case "stoplooking": CantFind.StopLooking(Get(f, "key")); return "/notfound";
				case "searchagain": // 2026 (1.3.51): like the Downloads tab - delete it and look for another copy
				{
					ITorrentClient c2 = Client();
					string h2 = Get(f, "hash");
					bool ok2 = c2 != null && c2.Remove(h2, true);
					if (ok2) Grabbed.RequestRetry(h2);
					return "/downloads?msg=" + Uri.EscapeDataString(ok2 ? "Deleted - another copy will be looked for" : "Could not reach the torrent program");
				}
				case "pause":
				case "resume":
				case "remove":
				case "removefiles":
					ITorrentClient client = Client();
					string h = Get(f, "hash");
					bool ok = client != null && (a == "pause" ? client.Pause(h) : a == "resume" ? client.Resume(h) : client.Remove(h, a == "removefiles"));
					return "/downloads?msg=" + Uri.EscapeDataString(ok ? "Done" : "Could not reach the torrent program");
				case "download":
					string hash;
					string url = Get(f, "url");
					hash = "";
					bool started = TorrentEngine.Instance() != null && TorrentEngine.Instance().StartDownloadingToFolder(out hash, url, "", true, "", "", "", "", "", url.StartsWith("magnet:") ? "magnet" : "torrent");
					if (started) PostProcessor.Remember(hash, "", Get(f, "title"), Get(f, "title"));
					return "/search?msg=" + Uri.EscapeDataString(started ? "Download started: " + Get(f, "title") : "Could not start it");
				case "settings":
					foreach (string k in new[] { "AutoGrabber.AutoAddRunning", "AutoGrabber.Quality", "AutoGrabber.MaxSizeMB", "AutoGrabber.MinSeeders", "Films.Quality", "Films.MaxSizeMB", "Films.FileInto", "AutoGrabber.Sound", "Films.Sound", "Films.UpgradeDeleteOld", "Films.UpgradeAuto", "TV.NightScan", "Films.MinSizeMB", "Films.NamePattern", "Films.FolderPattern", "Finished.ShowFolderPattern", "Finished.DropFolder", "AutoGrabber.MaxDownloading", "Finished.NearlyDoneHours" })
						if (f.ContainsKey(k)) Configuration.SetExtra(k, Get(f, k));
					return "/settings?msg=Saved";
				case "removesite": return "/sites?msg=" + Uri.EscapeDataString(Jackett("DELETE", "indexers/" + Get(f, "id"), null) ? Get(f, "id") + " removed" : "Jackett would not do it (does it have an admin password?)");
				case "addsite":
					string id = Get(f, "id");
					string cfg = JackettText("GET", "indexers/" + id + "/config");
					bool addOk = cfg != null && Jackett("POST", "indexers/" + id + "/config", cfg);
					return "/sites?msg=" + Uri.EscapeDataString(addOk ? id + " added" : "Jackett would not add it");
				case "updatejackett": return "/sites?msg=" + Uri.EscapeDataString(Jackett("POST", "server/update", "") ? "Jackett is updating itself" : "Jackett would not update");
				}
			}
			catch (Exception ex)
			{
				return path + "?msg=" + Uri.EscapeDataString("Something went wrong: " + ex.Message);
			}
			return path;
		}

		private static ITorrentClient _client;

		private static ITorrentClient Client()
		{
			if (TorrentEngine.Instance()?.TorrentSession != null) return TorrentEngine.Instance().TorrentSession;
			try
			{
				if (_client == null)
				{
					ConfigData c = Configuration.Instance().LoadConfiguration();
					_client = TorrentEngine.CreateClient(c.Server, int.Parse(c.Port), c.User, c.Password, c.Backend);
					_client.Connect();
				}
				return _client;
			}
			catch
			{
				_client = null;
				return null;
			}
		}

		private static string JackettText(string method, string api)
		{
			try
			{
				HttpWebRequest r = (HttpWebRequest)WebRequest.Create(JackettAggregateEngine.apiUrl + api);
				r.Method = method;
				r.Timeout = 30000;
				using (WebResponse w = r.GetResponse())
				using (StreamReader sr = new StreamReader(w.GetResponseStream()))
					return sr.ReadToEnd();
			}
			catch
			{
				return null;
			}
		}

		private static bool Jackett(string method, string api, string jsonBody)
		{
			try
			{
				HttpWebRequest r = (HttpWebRequest)WebRequest.Create(JackettAggregateEngine.apiUrl + api);
				r.Method = method;
				r.Timeout = 60000;
				if (jsonBody != null)
				{
					byte[] b = Encoding.UTF8.GetBytes(jsonBody);
					r.ContentType = "application/json";
					r.ContentLength = b.Length;
					using (Stream s = r.GetRequestStream()) s.Write(b, 0, b.Length);
				}
				using (HttpWebResponse w = (HttpWebResponse)r.GetResponse()) return (int)w.StatusCode < 300;
			}
			catch
			{
				return false;
			}
		}

		// ------------------------------------------------------------------ pages

		private static string Button(string action, string label, params string[] fields)
		{
			StringBuilder sb = new StringBuilder("<form method=post class=inline><input type=hidden name=a value=\"" + action + "\">");
			for (int i = 0; i + 1 < fields.Length; i += 2) sb.Append("<input type=hidden name=\"" + H(fields[i]) + "\" value=\"" + H(fields[i + 1]) + "\">");
			sb.Append("<button>" + H(label) + "</button></form>");
			return sb.ToString();
		}

		private static string Page(string path, Dictionary<string, string> f)
		{
			StringBuilder b = new StringBuilder();
			string msg = Get(f, "msg");
			switch (path.TrimEnd('/'))
			{
			case "":
			case "/shows": Shows(b); path = "/shows"; break;
			case "/movies": Movies(b); break;
			case "/notfound": NotFound(b); break;
			case "/downloads": Downloads(b); break;
			case "/search": Search(b, Get(f, "q"), Get(f, "kind")); break;
			case "/sites": Sites(b); break;
			case "/settings": Settings(b); break;
			case "/library": Library(b); break;
			case "/checkup": CheckUpPage(b); break;
			default: b.Append("<p>Page not found.</p>"); break;
			}
			string[][] nav = { new[] { "/movies", "Wanted movies" }, new[] { "/library", "My movies" }, new[] { "/shows", "Show list" }, new[] { "/checkup", "Check-up" }, new[] { "/notfound", "Not found" }, new[] { "/downloads", "Downloads" },
				new[] { "/search", "Search" }, new[] { "/sites", "Search sites" }, new[] { "/settings", "Settings" } };
			string menu = string.Join("", nav.Select(n => "<a href=\"" + n[0] + "\"" + (n[0] == path ? " class=on" : "") + ">" + n[1] + "</a>"));
			bool mp = System.Diagnostics.Process.GetProcessesByName("MediaPortal").Length > 0;
			return "<!DOCTYPE html><html><head><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\"><title>MyTorrents</title><style>" +
				"body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f4f5f7;color:#222}header{background:#1f3a5f;color:#fff;padding:10px 16px}header b{font-size:18px}" +
				"nav{background:#2c4f7c;padding:0 8px;overflow-x:auto;white-space:nowrap}nav a{color:#dfe8f5;text-decoration:none;display:inline-block;padding:10px 10px}nav a.on{background:#f4f5f7;color:#1f3a5f;font-weight:600}" +
				"main{padding:14px 16px;max-width:1100px}table{border-collapse:collapse;width:100%;background:#fff}td,th{padding:6px 8px;border-bottom:1px solid #e3e5e8;text-align:left;vertical-align:top;font-size:14px}" +
				"th{background:#eef1f5}.inline{display:inline}button{margin:1px 2px;padding:4px 9px;border:1px solid #9aa9bd;border-radius:4px;background:#fff;cursor:pointer}button:hover{background:#e8eef7}" +
				".msg{background:#e3f4e1;border:1px solid #9ccf98;padding:8px 10px;margin-bottom:10px}.dim{color:#777}.warn{color:#b35c00}input,select{padding:4px;margin:2px}" +
				"th[data-arrow]:after{content:attr(data-arrow)}.bar{margin:6px 0}.cols label{margin-right:8px;font-size:13px;white-space:nowrap}.small{font-size:12px}</style></head><body>" +
				"<header><b>MyTorrents</b> <span class=dim style=\"color:#b8c8de\">on " + H(Dns.GetHostName()) + " &middot; " + (mp ? "MediaPortal is running" : "MediaPortal is closed - the background helper is doing the work") + "</span></header>" +
				"<nav>" + menu + "</nav><main>" + (msg.Length > 0 ? "<div class=msg>" + H(msg) + "</div>" : "") + b + "</main><script>" + TableScript + "</script></body></html>";
		}

		private const string TableScript = @"
// 2026 (1.3.51): every table - click a heading to sort (again = the other way), a box to find rows, and
// ""Columns"" to choose which columns show; remembered in this browser
(function(){
 function key(t,i){return 'mt:'+location.pathname+':'+i;}
 function val(td){var v=td.getAttribute('data-v');if(v!==null)return parseFloat(v);var s=td.textContent.trim();var n=s.replace(/[,%]/g,'');if(n!==''&&!isNaN(n))return parseFloat(n);return s.toLowerCase();}
 var tables=document.querySelectorAll('main table');
 for(var ti=0;ti<tables.length;ti++)(function(t,ti){
  var rows=t.rows;if(rows.length<2)return;var head=rows[0];if(!head.cells.length||head.cells[0].tagName!='TH')return;
  var k=key(t,ti),st={};try{st=JSON.parse(localStorage.getItem(k)||'{}')}catch(e){}
  function save(){try{localStorage.setItem(k,JSON.stringify(st))}catch(e){}}
  function sort(c,dir){var body=[].slice.call(t.rows,1);body.sort(function(a,b){var x=val(a.cells[c]||a),y=val(b.cells[c]||b);if(typeof x!=typeof y){x=String(x);y=String(y);}return (x<y?-1:x>y?1:0)*dir;});
   for(var i=0;i<body.length;i++)t.tBodies[0].appendChild(body[i]);
   for(var h=0;h<head.cells.length;h++)head.cells[h].setAttribute('data-arrow',h==c?(dir>0?' ▲':' ▼'):'');}
  function hide(){for(var r=0;r<t.rows.length;r++)for(var c=0;c<t.rows[r].cells.length;c++)t.rows[r].cells[c].style.display=(st.h||[]).indexOf(c)>=0?'none':'';}
  for(var c=0;c<head.cells.length;c++)(function(c){var th=head.cells[c];if(!th.textContent.trim())return;th.style.cursor='pointer';th.title='Click to sort';
   th.onclick=function(){st.dir=(st.col===c?-(st.dir||1):1);st.col=c;save();sort(c,st.dir);};})(c);
  var bar=document.createElement('div');bar.className='bar';
  var f=document.createElement('input');f.placeholder='Find...';f.size=24;f.oninput=function(){var q=f.value.toLowerCase();for(var r=1;r<t.rows.length;r++)t.rows[r].style.display=t.rows[r].textContent.toLowerCase().indexOf(q)>=0?'':'none';};
  var cb=document.createElement('button');cb.type='button';cb.textContent='Columns';var box=document.createElement('span');box.className='cols';box.style.display='none';
  for(var c=0;c<head.cells.length;c++)(function(c){var n=head.cells[c].textContent.trim();if(!n)return;var l=document.createElement('label');var x=document.createElement('input');x.type='checkbox';x.checked=(st.h||[]).indexOf(c)<0;
   x.onchange=function(){st.h=st.h||[];var i=st.h.indexOf(c);if(x.checked&&i>=0)st.h.splice(i,1);if(!x.checked&&i<0)st.h.push(c);save();hide();};l.appendChild(x);l.appendChild(document.createTextNode(' '+n+'  '));box.appendChild(l);})(c);
  cb.onclick=function(){box.style.display=box.style.display=='none'?'inline':'none';};
  if(rows.length>6)bar.appendChild(f);bar.appendChild(cb);bar.appendChild(box);t.parentNode.insertBefore(bar,t);
  if(st.col!==undefined)sort(st.col,st.dir||1);hide();
 })(tables[ti],ti);
})();
// 2026 (1.3.64): pressing a button (Find 5.1, Not this one, Add...) comes back to the same spot on the page,
// not the top - remembered just for that one page load
(function(){
 try{var s=JSON.parse(sessionStorage.getItem('mt:scroll')||'null');sessionStorage.removeItem('mt:scroll');
  if(s&&s.p===location.pathname)window.scrollTo(0,s.y);}catch(e){}
 var fs=document.querySelectorAll('form');
 for(var i=0;i<fs.length;i++)fs[i].addEventListener('submit',function(e){if((e.target.method||'').toLowerCase()!='post')return;
  try{sessionStorage.setItem('mt:scroll',JSON.stringify({p:location.pathname,y:window.scrollY}))}catch(x){}});
})();
";

		private static void Shows(StringBuilder b)
		{
			List<AutoGrabberStatus.Show> shows = AutoGrabberStatus.Read().OrderBy(x => WatchListView.SortName(x.Name)).ToList();
			b.Append("<h2>Show list (" + shows.Count + ")</h2><p>" + Button("checkall", "Check all now") + "</p><table><tr><th>Show</th><th>Looks for</th><th>Next</th><th>Missing</th><th>Which</th><th>Airs</th><th>Last look</th><th></th></tr>");
			foreach (AutoGrabberStatus.Show x in shows)
			{
				string which = x.Missing.Length > 60 ? x.Missing.Substring(0, 60).Replace(",", ", ") + "..." : x.Missing.Replace(",", ", ");
				long tvdbId;
				string links = " <a class=small href=\"" + H(long.TryParse(x.Id, out tvdbId) ? "https://thetvdb.com/dereferrer/series/" + tvdbId : "https://thetvdb.com/search?query=" + Uri.EscapeDataString(x.Name)) + "\" target=_blank>TVDB</a>" +
					" <a class=small href=\"https://www.themoviedb.org/search/tv?query=" + H(Uri.EscapeDataString(x.SearchName.Length > 0 ? x.SearchName : x.Name)) + "\" target=_blank>TMDb</a>"; // 1.3.52
				b.Append("<tr><td>" + H(x.Name) + links + "</td><td class=dim>" + H(x.Type) + "</td><td>" + H(x.Next.Length > 0 ? x.Next : "-") + "</td><td data-v=\"" + x.MissingCount + "\">" + (x.MissingCount > 0 ? "<span class=warn title=\"" + H(x.Missing) + "\">" + x.MissingCount + "</span>" : "-") +
					"</td><td class=\"dim small\" title=\"" + H(x.Missing) + "\">" + H(which) + "</td><td data-v=\"" + (x.AirsAt == DateTime.MaxValue ? "99999999999" : x.AirsAt.Ticks / 10000000L + "") + "\">" + H(x.Airs) + "</td><td class=dim data-v=\"" + (x.LookedAt.Ticks / 10000000L) + "\">" + H(x.LastLook) + "</td><td>" + Button("check", "Check now", "id", x.Id, "name", x.Name) +
					(x.MissingCount > 0 ? Button("grabmissing", "Grab the missing ones", "id", x.Id, "name", x.Name) : "") + "</td></tr>");
			}
			b.Append("</table>");
			if (shows.Count == 0) b.Append("<p class=dim>MediaPortal (or the background helper) writes this list while it runs.</p>");
		}

		private static void Movies(StringBuilder b)
		{
			List<MovieFiling.Pending> waiting = MovieFiling.Waiting();
			if (waiting.Count > 0)
			{
				b.Append("<h2>New movies waiting for your OK (" + waiting.Count + ")</h2><table>");
				foreach (MovieFiling.Pending p in waiting) b.Append("<tr><td>" + H(p.Title) + "</td><td class=dim>" + H(p.To) + "</td><td>" + Button("filemovie", "File it", "from", p.From) + Button("leavemovie", "Leave it", "from", p.From) + "</td></tr>");
				b.Append("</table>");
			}
			List<WantedFilms.Film> films = WantedFilms.Read();
			b.Append("<h2>Wanted movies (" + films.Count + ")</h2><form method=post><input type=hidden name=a value=addmovie>Title <input name=title size=30> Year <input name=year size=5> <button>Add</button></form>");
			b.Append("<p>" + Button("reread", "Read my movies again") + "</p><table><tr><th>Movie</th><th>Year</th><th>State</th><th>Added</th><th>Last look</th><th></th></tr>");
			foreach (WantedFilms.Film x in films)
				b.Append("<tr><td>" + H(x.Title + (x.Replace.Length > 0 ? (x.Best ? " - better copy (5.1 if there is one)" : x.Better ? " - better copy" : " - 5.1 upgrade") : "")) +
					" <a class=small href=\"https://www.themoviedb.org/search/movie?query=" + H(Uri.EscapeDataString(x.Title)) + (x.Year.Length > 0 ? "%20y:" + H(x.Year) : "") + "\" target=_blank>TMDb</a></td><td>" + H(x.Year) + "</td><td class=dim>" + H(WantedFilms.StateText(x)) + "</td><td class=dim data-v=\"" + (x.Added.Ticks / 10000000L) + "\">" + x.Added.ToString("d MMM yyyy") +
					"</td><td class=dim data-v=\"" + (x.LastLook.Ticks / 10000000L) + "\">" + (x.LastLook == DateTime.MinValue ? "-" : x.LastLook.ToString("ddd d MMM h:mm tt")) + "</td><td>" + Button("lookmovie", "Look now", "title", x.Title, "year", x.Year) + Button("removemovie", "Remove", "title", x.Title, "year", x.Year) + "</td></tr>");
			b.Append("</table>");
		}

		/// 2026 (1.3.49): the check-up.
		private static void CheckUpPage(StringBuilder b)
		{
			b.Append("<h2>" + H(CheckUp.Summary()) + "</h2><p>" + Button("checknow", "Check now") + " <span class=dim>checked " + H(CheckUp.LastRun > DateTime.MinValue ? CheckUp.LastRun.ToString("ddd d MMM h:mm tt") : "never") + "</span></p><table>");
			foreach (CheckUp.Finding x in CheckUp.Read().OrderBy(x => x.Level == "fix" ? 0 : x.Level == "note" ? 1 : 2))
			{
				string label = x.Action == "import" ? "Add to MP-TVSeries" : x.Action == "dupe" ? "Delete this copy" : x.Action == "better" ? "Find a better copy" : x.Action == "leftover" ? "Delete it" : "";
				b.Append("<tr><td" + (x.Level == "fix" ? " class=warn" : " class=dim") + ">" + (x.Level == "fix" ? "Needs you" : x.Level == "note" ? "Note" : "OK") + "</td><td>" + H(x.Area) + "</td><td title=\"" + H(x.File) + "\">" + H(x.Text) + "</td><td>" +
					(label.Length > 0 ? Button("cufix", label, "key", x.Key) : "") + (x.Level != "ok" && x.Key.Length > 0 ? Button("cuignore", "Ignore", "key", x.Key) : "") + "</td></tr>");
			}
			b.Append("</table>");
		}

		/// 2026 (1.3.46): collections you could make, and stereo-only movies to find in 5.1.
		private static void Library(StringBuilder b)
		{
			List<Collections.Suggestion> coll = Collections.Suggestions();
			b.Append("<h2>Collections you could make (" + coll.Count + ")</h2><p class=dim>From Moving Pictures. \"Move them\" moves the movies' folders into " + H(Collections.Root) + ".</p><table><tr><th>Collection</th><th>Your movies</th><th></th></tr>");
			foreach (Collections.Suggestion c in coll)
				b.Append("<tr><td>" + H(c.Name) + (c.FolderExists ? " <span class=dim>(add to yours)</span>" : "") + "</td><td class=dim>" + H(string.Join(", ", c.Films.Select(x => x.Display))) + "</td><td>" +
					Button("makecoll", "Move them", "name", c.Name) + Button("missingcoll", "What's missing", "name", c.Name) + Button("ignorecoll", "Not this one", "name", c.Name) + "</td></tr>");
			b.Append("</table>");
			// 2026 (1.3.65): poor copies and stereo-only movies in one list, with the year to sort by
			List<Improve.Item> imp = Improve.List();
			b.Append("<h2>Movies to improve (" + imp.Count + ")</h2><p class=dim>Poor copies (too small, or a lower picture than " + H(Films.Quality) + ") and stereo-only movies. " +
				"The button looks for the best it can: a poor copy that's also stereo gets a better copy - 5.1 if there is one, a better stereo one if not. " +
				(SoundUpgrades.DeleteOld ? "The old copy is deleted once the new one is in place." : "Both copies are kept.") + " Click a heading to sort.</p>" +
				"<table><tr><th>Movie</th><th>Year</th><th>Picture</th><th>Sound</th><th>Size</th><th></th></tr>");
			foreach (Improve.Item m in imp)
				b.Append("<tr><td>" + H(m.Title) + "</td><td>" + (m.Year > 0 ? m.Year.ToString() : "") + "</td><td" + (m.Poor.Length > 0 ? "" : " class=dim") + ">" + H(m.PictureText + (m.Resolution.Length > 0 && m.Poor.Length == 0 ? " (" + m.Resolution + ")" : "")) +
					"</td><td" + (m.Stereo ? "" : " class=dim") + ">" + H(m.SoundText) + "</td><td class=dim data-v=\"" + m.Size + "\">" + (m.Size > 0 ? Films.SizeText(m.Size / 1024 / 1024) : "") + "</td><td>" +
					Button("improve", m.Button, "file", m.File) + Button("ignoreimprove", "Not this one", "file", m.File) + "</td></tr>");
			b.Append("</table>");
			// 2026 (1.3.65): the ones you said "Not this one" to - look them over, put them back
			List<Improve.Item> hid = Improve.Hidden();
			b.Append("<h3>Hidden - you said \"Not this one\" (" + hid.Count + ")</h3>");
			if (hid.Count == 0) b.Append("<p class=dim>None.</p>");
			else
			{
				b.Append("<p class=dim>\"Put back\" returns it to Movies to improve (if it still needs it). " + Button("putbackall", "Put them all back") + "</p><table><tr><th>Movie</th><th>Year</th><th>Size</th><th></th></tr>");
				foreach (Improve.Item h in hid)
					b.Append("<tr><td>" + H(h.Title) + "</td><td>" + (h.Year > 0 ? h.Year.ToString() : "") + "</td><td class=dim data-v=\"" + h.Size + "\">" + (h.Size > 0 ? Films.SizeText(h.Size / 1024 / 1024) : "") + "</td><td>" + Button("putback", "Put back", "file", h.File) + "</td></tr>");
				b.Append("</table>");
			}
		}

		private static void NotFound(StringBuilder b)
		{
			List<CantFind.Item> eps = CantFind.AllNotFound();
			List<WantedFilms.Film> films = WantedFilms.Read().Where(x => x.State == "wanted").ToList();
			b.Append("<h2>Not found yet</h2><h3>Movies (" + films.Count + ")</h3><table>");
			foreach (WantedFilms.Film x in films) b.Append("<tr><td>" + H(x.Display) + "</td><td class=dim>" + H(WantedFilms.StateText(x)) + "</td></tr>");
			b.Append("</table><h3>Episodes (" + eps.Count + ")</h3><table><tr><th>Episode</th><th>Looking for</th><th>Why</th><th></th></tr>");
			foreach (CantFind.Item x in eps)
				b.Append("<tr><td>" + H(x.What) + "</td><td>" + H(x.Since) + "</td><td class=dim>" + H(x.Why) + "</td><td>" + Button("looknow", "Look now", "key", x.Key, "id", x.ShowId, "name", x.Name) + Button("stoplooking", "Stop looking", "key", x.Key) + "</td></tr>");
			b.Append("</table>");
		}

		private static string Size(long bytes) => bytes <= 0 ? "?" : bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture) + " GB" : (bytes / (1L << 20)) + " MB";

		private static void Downloads(StringBuilder b)
		{
			ITorrentClient client = Client();
			List<Torrent> all = new List<Torrent>();
			try
			{
				if (client != null && (client == TorrentEngine.Instance()?.TorrentSession || client.UpdateData())) all = client.TorrentsAll.Cast<Torrent>().ToList();
			}
			catch
			{
			}
			if (client == null || all.Count == 0 && client != TorrentEngine.Instance()?.TorrentSession)
			{
				b.Append("<p class=warn>Can't reach the torrent program - is it running?</p>");
			}
			foreach (bool done in new[] { false, true })
			{
				List<Torrent> list = all.Where(t => (t.Progress >= 100.0) == done).OrderByDescending(t => t.AddedOn).ToList();
				b.Append("<h2>" + (done ? "Seeding and finished" : "Downloading") + " (" + list.Count + ")</h2><table><tr><th>Name</th><th>Size</th><th>Done</th><th>Status</th><th>Down</th><th>Up</th><th>Seeds</th><th>Peers</th><th>Seeds:Peers</th><th>Ratio</th><th>Time left</th><th>Library</th><th>Added</th><th></th></tr>");
				foreach (Torrent t in list)
				{
					// 2026 (1.3.51): the same columns as the Downloads tab in MyTorrents Configuration
					string[] c = DownloadsPage.Cells(t);
					DateTime added = t.AddedOn > 0 ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(t.AddedOn).ToLocalTime() : DateTime.MinValue;
					bool ours = Grabbed.FindByHash(t.Hash) != null;
					b.Append("<tr><td>" + H(PostProcessor.DisplayName(t)) + "</td><td data-v=\"" + t.Size + "\">" + H(c[1]) + "</td><td data-v=\"" + t.Progress.ToString("0.0", CultureInfo.InvariantCulture) + "\">" + H(c[2]) + "</td><td class=" + (c[3].StartsWith("No seeds") || c[3].StartsWith("Fetching") ? "warn" : "dim") + ">" + H(c[3]) +
						"</td><td data-v=\"" + t.DownloadSpeed + "\">" + H(c[4]) + "</td><td data-v=\"" + t.UploadSpeed + "\">" + H(c[5]) + "</td><td data-v=\"" + t.SeedsConnected + "\">" + H(c[6]) + "</td><td data-v=\"" + t.PeersConnected + "\">" + H(c[10]) + "</td><td data-v=\"" + DownloadsPage.SeedPeer(t).ToString("0.00", CultureInfo.InvariantCulture) + "\">" + H(c[11]) + "</td><td data-v=\"" + t.Ratio + "\">" + H(c[7]) +
						"</td><td data-v=\"" + (t.Progress >= 100 ? -1 : t.ETA > 0 ? t.ETA : 999999999) + "\">" + H(c[8]) + "</td><td>" + H(c[9]) + "</td><td class=dim data-v=\"" + t.AddedOn + "\">" + (added == DateTime.MinValue ? "" : added.ToString("d MMM h:mm tt")) + "</td><td>" +
						(t.HasStatus(ClientSupport.Paused) ? Button("resume", "Resume", "hash", t.Hash) : Button("pause", "Pause", "hash", t.Hash)) +
						(ours && !done ? Button("searchagain", "Delete and search again", "hash", t.Hash) : "") +
						Button("remove", "Remove", "hash", t.Hash) + Button("removefiles", "Remove + files", "hash", t.Hash) + "</td></tr>");
				}
				b.Append("</table>");
			}
		}

		private static void Search(StringBuilder b, string q, string kind)
		{
			b.Append("<h2>Search</h2><form method=get action=/search><input name=q size=40 value=\"" + H(q) + "\"> <select name=kind><option value=tv" + (kind == "tv" ? " selected" : "") + ">TV</option><option value=movie" + (kind == "movie" ? " selected" : "") +
				">Movie</option><option value=any" + (kind == "any" ? " selected" : "") + ">Anything</option></select> <button>Search</button></form>");
			if (q.Trim().Length == 0) return;
			TorrentSearchEngine engines = new TorrentSearchEngine(Configuration.Instance().Settings["TorrentSearch.Config"] as string);
			ITorrentSearch search = engines.Engines.OfType<TorrentSearch_Combiner>().FirstOrDefault(c => c.Name == TorrentSearchEngine.TVSeriesEngineName && c.Engines.Count > 0);
			TorrentMatchList results = search?.Search(q, "Default", null) ?? new TorrentMatchList(null);
			if (kind == "movie") results = Films.Apply(results) ?? results;
			List<TorrentMatch> list = results.OrderByDescending(m => m.Seed).Take(80).ToList();
			b.Append("<p class=dim>" + results.Count + " found" + (results.Count > 80 ? ", the 80 with most seeds shown" : "") + "</p><table><tr><th>Copy</th><th>Size</th><th>Seeds</th><th>Site</th><th></th></tr>");
			foreach (TorrentMatch m in list)
			{
				string link = string.IsNullOrEmpty(m.Url) ? m.magnet : m.Url;
				b.Append("<tr><td>" + H(m.Title) + "</td><td>" + Size(m.Size) + "</td><td>" + m.Seed + "</td><td class=dim>" + H(m.tracker) + "</td><td>" + Button("download", "Download", "url", link, "title", m.Title) + "</td></tr>");
			}
			b.Append("</table>");
		}

		private static void Sites(StringBuilder b)
		{
			// results / timeouts per site, from MyTorrents.log (as the Downloads page counts them)
			Dictionary<string, int[]> stats = SiteStats();
			b.Append("<h2>Search sites</h2><p>" + Button("updatejackett", "Update Jackett (its sites update with it)") + "</p><table><tr><th>Site</th><th>Results lately</th><th>Timed out</th><th></th></tr>");
			string list = JackettText("GET", "indexers?configured=true");
			List<KeyValuePair<string, string>> configured = new List<KeyValuePair<string, string>>();
			if (list != null)
			{
				foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(list, "\"id\"\\s*:\\s*\"([^\"]+)\"[^{}]*?\"name\"\\s*:\\s*\"([^\"]+)\""))
					configured.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
			}
			if (configured.Count == 0) b.Append("<tr><td colspan=4 class=warn>Jackett didn't answer the site list (it may have an admin password). The counts below come from MyTorrents' log.</td></tr>");
			IEnumerable<string> names = configured.Count > 0 ? configured.Select(c => c.Value) : stats.Keys;
			foreach (string name in names.OrderByDescending(n => stats.ContainsKey(n) ? stats[n][0] : 0))
			{
				int[] s = stats.ContainsKey(name) ? stats[name] : new[] { 0, 0, 0 };
				string id = configured.FirstOrDefault(c => c.Value == name).Key;
				b.Append("<tr><td>" + H(name) + "</td><td>" + s[0] + " in " + s[2] + " searches</td><td" + (s[1] > s[2] / 4 ? " class=warn" : "") + ">" + s[1] + "</td><td>" + (id != null ? Button("removesite", "Remove", "id", id) : "") + "</td></tr>");
			}
			b.Append("</table><h3>Add a site</h3><form method=post><input type=hidden name=a value=addsite>Jackett's name for it (e.g. torrentdownload) <input name=id size=20> <button>Add</button></form>");
		}

		internal static Dictionary<string, int[]> SiteStats()
		{
			Dictionary<string, int[]> d = new Dictionary<string, int[]>();
			try
			{
				List<string> last = new List<string>();
				foreach (string log in Log.Files().Skip(Math.Max(0, Log.Files().Count - 2))) // 2026 (1.3.55): today and yesterday
				using (FileStream fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					if (fs.Length > 4 * 1024 * 1024) fs.Seek(-4 * 1024 * 1024, SeekOrigin.End);
					using (StreamReader sr = new StreamReader(fs))
					{
						string line;
						while ((line = sr.ReadLine()) != null) if (line.Contains("sites answered in time")) last.Add(line);
					}
				}
				foreach (string line in last.Skip(Math.Max(0, last.Count - 300)))
				{
					int dash = line.IndexOf(" results - ", StringComparison.Ordinal);
					if (dash < 0) continue;
					foreach (string part in line.Substring(dash + 11).Split(new[] { ", " }, StringSplitOptions.None))
					{
						int sp = part.LastIndexOf(' ');
						if (sp < 0) continue;
						string name = part.Substring(0, sp).Trim(), v = part.Substring(sp + 1).Trim();
						if (!d.ContainsKey(name)) d[name] = new int[3];
						d[name][2]++;
						if (v == "x") d[name][1]++;
						else if (int.TryParse(v, out int n)) d[name][0] += n;
					}
				}
			}
			catch
			{
			}
			return d;
		}

		private static string Select(string name, string current, string[] values, string[] labels)
		{
			StringBuilder s = new StringBuilder("<select name=\"" + name + "\">");
			for (int i = 0; i < values.Length; i++) s.Append("<option value=\"" + H(values[i]) + "\"" + (values[i] == current ? " selected" : "") + ">" + H(labels[i]) + "</option>");
			return s.Append("</select>").ToString();
		}

		private static void Settings(StringBuilder b)
		{
			string[] q = SeriesItem.QualityChoices;
			b.Append("<h2>Settings</h2><p class=dim>The main ones - everything else is in MyTorrents Configuration on the HTPC.</p><form method=post><input type=hidden name=a value=settings><table>");
			b.Append("<tr><td>How MyTorrents works</td><td>" + Select("AutoGrabber.AutoAddRunning", AutoAdd.Enabled ? "true" : "false", new[] { "true", "false" }, new[] { "Automatic - keeps my running shows up to date", "Manual - only what I ask for" }) + "</td></tr>");
			b.Append("<tr><td>TV quality</td><td>" + Select("AutoGrabber.Quality", SeriesItem.Quality, q, q) + "</td></tr>");
			b.Append("<tr><td>Biggest episode (MB, 0 = no limit)</td><td><input name=\"AutoGrabber.MaxSizeMB\" size=6 value=\"" + SeriesItem.MaxSizeMB + "\"></td></tr>");
			b.Append("<tr><td>Minimum seeders</td><td><input name=\"AutoGrabber.MinSeeders\" size=3 value=\"" + SeriesItem.MinSeeders + "\"></td></tr>");
			b.Append("<tr><td>TV sound</td><td>" + Select("AutoGrabber.Sound", Sound.Tv, Sound.Choices, Sound.Choices.Select(Sound.Text).ToArray()) + "</td></tr>");
			b.Append("<tr><td>Movie quality</td><td>" + Select("Films.Quality", Films.Quality, q, q) + "</td></tr>");
			b.Append("<tr><td>Biggest movie (MB, 0 = no limit)</td><td><input name=\"Films.MaxSizeMB\" size=6 value=\"" + Films.MaxSizeMB + "\"></td></tr>");
			b.Append("<tr><td>Movie sound</td><td>" + Select("Films.Sound", Sound.Movies, Sound.Choices, Sound.Choices.Select(Sound.Text).ToArray()) + "</td></tr>");
			b.Append("<tr><td>5.1 upgrade - the stereo copy</td><td>" + Select("Films.UpgradeDeleteOld", SoundUpgrades.DeleteOld ? "true" : "false", new[] { "true", "false" }, new[] { "Delete it once the 5.1 one is in", "Keep both" }) + "</td></tr>");
			b.Append("<tr><td>Downloads at once</td><td>" + Select("AutoGrabber.MaxDownloading", Busy.Max.ToString(), Busy.Choices.Select(x => x.ToString()).ToArray(), Busy.Choices.Select(Busy.Text).ToArray()) + "</td></tr>");
			b.Append("<tr><td>Nearly finished (99%+, no seeds)</td><td>" + Select("Finished.NearlyDoneHours", PostProcessor.NearlyDoneHours.ToString(), PostProcessor.NearlyDoneChoices.Select(x => x.ToString()).ToArray(), PostProcessor.NearlyDoneChoices.Select(PostProcessor.NearlyDoneText).ToArray()) + "</td></tr>");
			b.Append("<tr><td>Drop folder (empty = off)</td><td><input name=\"Finished.DropFolder\" size=40 value=\"" + H(DropFolder.Folder) + "\"></td></tr>");
			b.Append("<tr><td>New show folders</td><td>" + Select("Finished.ShowFolderPattern", Naming.ShowFolderPattern, Naming.ShowFolderStyles.Select(x => x[1]).ToArray(), Naming.ShowFolderStyles.Select(x => x[0]).ToArray()) + "</td></tr>");
			b.Append("<tr><td>Movie names</td><td>" + Select("Films.NamePattern", Naming.MoviePattern, Naming.MovieStyles.Select(x => x[1]).ToArray(), Naming.MovieStyles.Select(x => x[0]).ToArray()) + "</td></tr>");
			b.Append("<tr><td>Movie folders</td><td>" + Select("Films.FolderPattern", Naming.MovieFolderPattern, Naming.MovieFolderStyles.Select(x => x[1]).ToArray(), Naming.MovieFolderStyles.Select(x => x[0]).ToArray()) + "</td></tr>");
			b.Append("<tr><td>Smallest movie</td><td>" + Select("Films.MinSizeMB", Films.MinSizeSetting.ToString(), Films.MinSizeChoices.Select(x => x.ToString()).ToArray(), Films.MinSizeChoices.Select(Films.MinSizeText).ToArray()) + "</td></tr>");
			b.Append("<tr><td>MP-TVSeries scan (2-5 am)</td><td>" + Select("TV.NightScan", NightScan.Mode, NightScan.Choices, NightScan.Choices.Select(NightScan.Text).ToArray()) + "</td></tr>");
			b.Append("<tr><td>Look for 5.1 copies by itself</td><td>" + Select("Films.UpgradeAuto", SoundUpgrades.Auto ? "true" : "false", new[] { "false", "true" }, new[] { "No - only when I ask", "Yes, " + SoundUpgrades.AutoPerDay + " a day" }) + "</td></tr>");
			b.Append("<tr><td>New movies</td><td>" + Select("Films.FileInto", MovieFiling.Mode, new[] { "new", "library", "ask" }, new[] { "I file them myself", "File them for me", "File them for me, but show me first" }) + "</td></tr>");
			b.Append("</table><p><button>Save</button></p></form>");
		}
	}
}
