using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sohvaperuna.MyTorrents
{
	/// <summary>
	/// 2026 (1.3.46): "Collections you could make". Moving Pictures already records which collection each film belongs
	/// to (TMDb), so no lookups are needed: films outside your Collections folder that belong to a collection become
	///  - "belongs in your X collection" when Collections\X\ already exists, or
	///  - "make an X collection" when 2 or more of your films belong to it.
	/// Nothing moves without your OK. "What's missing" asks TMDb (through MP-TVSeries) for the collection's other films,
	/// to add to Wanted movies. Films / collections you say no to are kept in MyTorrents.collections-ignore.txt.
	/// </summary>
	public static class Collections
	{
		public sealed class Film
		{
			public string Title = "", File = "", ImdbId = "";
			public int Year;
			public string Display => Title + (Year > 0 ? " (" + Year + ")" : "");
		}

		public sealed class Suggestion
		{
			public string Name = "";     // the collection (as Moving Pictures / TMDb call it)
			public string Folder = "";   // Collections\<folder>
			public bool FolderExists;
			public List<Film> Films = new List<Film>();
			public string Text => (FolderExists ? "Belongs in your " + Path.GetFileName(Folder) + " collection" : "Make a " + Name + " collection") +
				" - " + Films.Count + (Films.Count == 1 ? " movie" : " movies") + ": " + string.Join(", ", Films.Select(f => f.Display));
		}

		private static string IgnoreFile => MediaPortal.Configuration.Config.GetFile(MediaPortal.Configuration.Config.Dir.Config, "MyTorrents.collections-ignore.txt");

		private static HashSet<string> Ignored()
		{
			try
			{
				return File.Exists(IgnoreFile) ? new HashSet<string>(File.ReadAllLines(IgnoreFile).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
			catch
			{
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
		}

		public static void Ignore(Suggestion s)
		{
			try
			{
				File.AppendAllText(IgnoreFile, s.Name + Environment.NewLine);
			}
			catch
			{
			}
		}

		/// Your Collections folder: the setting, else the Moving Pictures folder with "collection" in its name.
		public static string Root
		{
			get
			{
				if (MovieFiling.CollectionsFolder.Length > 0) return MovieFiling.CollectionsFolder.TrimEnd('\\');
				try
				{
					return (LibraryFolders.Movies().FirstOrDefault(p => (p ?? "").IndexOf("collection", StringComparison.OrdinalIgnoreCase) >= 0) ?? "").TrimEnd('\\');
				}
				catch
				{
					return "";
				}
			}
		}

		/// Everything to suggest, biggest first.
		public static List<Suggestion> Suggestions()
		{
			string root = Root;
			List<Suggestion> list = new List<Suggestion>();
			if (root.Length == 0) return list;
			HashSet<string> ignored = Ignored();
			List<string> folders = new List<string>();
			try
			{
				if (Directory.Exists(root)) folders = Directory.GetDirectories(root).ToList();
			}
			catch
			{
			}
			// title | year | collections | imdb | file
			List<string> rows = LibraryFolders.Read("movingpictures.db3",
				"select ifnull(m.title,'') || char(9) || ifnull(m.year,0) || char(9) || ifnull(m.collections,'') || char(9) || ifnull(m.imdb_id,'') || char(9) || ifnull(f.fullpath,'') " +
				"from movie_info m join local_media__movie_info l on l.movie_info_id = m.id join local_media f on f.id = l.local_media_id where f.ignored = 0 or f.ignored is null");
			Dictionary<string, Suggestion> byName = new Dictionary<string, Suggestion>(StringComparer.OrdinalIgnoreCase);
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string row in rows)
			{
				string[] p = row.Split('\t');
				if (p.Length < 5 || p[4].Length == 0) continue;
				if (p[4].StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) continue; // already in a collection
				foreach (string coll in p[2].Split('|').Select(c => c.Trim()).Where(c => c.Length > 0))
				{
					if (ignored.Contains(coll)) continue;
					string key = coll + "|" + p[0] + "|" + p[1];
					if (!seen.Add(key)) continue; // a film in two parts (CD1 / CD2) counts once
					Suggestion s;
					if (!byName.TryGetValue(coll, out s))
					{
						string match = MediaNames.Key(coll.Replace(" Collection", ""));
						string have = folders.FirstOrDefault(d => MediaNames.Key(Path.GetFileName(d)) == match || MediaNames.Key(Path.GetFileName(d).Replace(" Collection", "")) == match);
						s = new Suggestion { Name = coll, Folder = have ?? Path.Combine(root, MediaNames.SafeName(coll)), FolderExists = have != null };
						byName[coll] = s;
					}
					int y;
					int.TryParse(p[1], out y);
					s.Films.Add(new Film { Title = p[0], Year = y, ImdbId = p[3], File = p[4] });
				}
			}
			list.AddRange(byName.Values.Where(s => s.FolderExists || s.Films.Count >= 2));
			return list.OrderByDescending(s => s.FolderExists).ThenByDescending(s => s.Films.Count).ThenBy(s => s.Name).ToList();
		}

		/// OK - moves the films' folders into Collections\<collection>\. Returns "" or what went wrong.
		public static string Make(Suggestion s)
		{
			List<string> problems = new List<string>();
			foreach (Film f in s.Films)
			{
				try
				{
					string dir = Path.GetDirectoryName(f.File);
					bool ownFolder = !LibraryFolders.Movies().Any(r => string.Equals((r ?? "").TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
					if (ownFolder)
					{
						string to = Path.Combine(s.Folder, Path.GetFileName(dir));
						Move(dir, to, null);
					}
					else
					{
						// the file sits straight in the Movies folder: give it its own "Title (Year)" folder
						string to = Path.Combine(s.Folder, MediaNames.SafeName(f.Display));
						Move(dir, to, Path.GetFileNameWithoutExtension(f.File));
					}
				}
				catch (Exception ex)
				{
					problems.Add(f.Display + ": " + ex.Message);
				}
			}
			LibraryFolders.RereadLibrary();
			Log.Instance().Print("Collections: " + s.Name + " - moved " + (s.Films.Count - problems.Count) + " of " + s.Films.Count + " movies into " + s.Folder);
			return string.Join("\r\n", problems);
		}

		/// Moves a folder (or just the files named "baseName.*") - a rename on the same share, else copy, check, delete.
		private static void Move(string from, string to, string baseName)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(to));
			if (baseName == null)
			{
				if (Directory.Exists(to)) throw new IOException(to + " is already there");
				try
				{
					Directory.Move(from, to);
					return;
				}
				catch (IOException)
				{
				}
				CopyAll(from, to);
				Directory.Delete(from, true);
				return;
			}
			Directory.CreateDirectory(to);
			foreach (string f in Directory.GetFiles(from, baseName + ".*"))
			{
				string d = Path.Combine(to, Path.GetFileName(f));
				try
				{
					File.Move(f, d);
				}
				catch (IOException)
				{
					File.Copy(f, d, false);
					if (new FileInfo(d).Length != new FileInfo(f).Length) throw new IOException("copy of " + Path.GetFileName(f) + " is incomplete");
					File.Delete(f);
				}
			}
		}

		private static void CopyAll(string from, string to)
		{
			Directory.CreateDirectory(to);
			foreach (string f in Directory.GetFiles(from))
			{
				string d = Path.Combine(to, Path.GetFileName(f));
				File.Copy(f, d, false);
				if (new FileInfo(d).Length != new FileInfo(f).Length) throw new IOException("copy of " + Path.GetFileName(f) + " is incomplete");
			}
			foreach (string sub in Directory.GetDirectories(from)) CopyAll(sub, Path.Combine(to, Path.GetFileName(sub)));
		}

		/// The collection's other films you don't have (from TMDb, through MP-TVSeries) - for Wanted movies.
		public static List<Film> Missing(Suggestion s)
		{
			List<Film> missing = new List<Film>();
			try
			{
				object search = MovieFiling.TmdbCall("/search/collection?query=" + Uri.EscapeDataString(s.Name) + "&language=en-US");
				IList results = (search as IDictionary)?["results"] as IList;
				object first = results?.Cast<object>().FirstOrDefault();
				if (first == null) return missing;
				object coll = MovieFiling.TmdbCall("/collection/" + Convert.ToString((first as IDictionary)?["id"]) + "?language=en-US");
				IList parts = (coll as IDictionary)?["parts"] as IList;
				if (parts == null) return missing;
				foreach (object part in parts)
				{
					IDictionary d = part as IDictionary;
					string title = Convert.ToString(d?["title"] ?? "");
					string date = Convert.ToString(d?["release_date"] ?? "");
					int y = 0;
					if (date.Length >= 4) int.TryParse(date.Substring(0, 4), out y);
					DateTime released;
					if (!DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out released) || released > DateTime.Today) continue; // not out yet
					if (LibraryFolders.HaveFilm(title, y)) continue;
					missing.Add(new Film { Title = title, Year = y });
				}
			}
			catch (Exception ex)
			{
				Log.Instance().Print("Collections: could not ask TMDb about " + s.Name + ": " + ex.Message);
			}
			return missing;
		}
	}
}
