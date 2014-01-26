//
// Copyright (C) 2012 Timo Dörr
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Linq;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;
using System.IO;

using JsonFx;
using JsonFx.Json;
using System.Text.RegularExpressions;

namespace JsonConfig 
{
	public static class Config {
		public static dynamic Default = new ConfigObject ();
		public static dynamic User = new ConfigObject ();

		public static dynamic MergedConfig {
			get {
				return Merger.Merge (User, Default);
			}
		}

		public static string defaultEnding = ".conf";

		private static dynamic global_config;
		public static dynamic Global {
			get {
				if (global_config == null) {
					global_config = MergedConfig;
				}
				return global_config;
			}
			set {
				global_config = Merger.Merge (value, MergedConfig);
			}
		}
	
		/// <summary>
		/// Gets a ConfigObject that represents the current configuration. Since it is 
		/// a cloned copy, changes to the underlying configuration files that are done
		/// after GetCurrentScope() is called, are not applied in the returned instance.
		/// </summary>
		public static ConfigObject GetCurrentScope () {
			if (Global is NullExceptionPreventer)
				return new ConfigObject ();
			else
				return Global.Clone ();
		}

		public delegate void UserConfigFileChangedHandler ();
		public static event UserConfigFileChangedHandler OnUserConfigFileChanged;

		static Config ()
		{
			// static C'tor, run once to check for compiled/embedded config

			// TODO scan ALL linked assemblies and merge their configs
			var assembly = System.Reflection.Assembly.GetCallingAssembly ();
			Default = GetDefaultConfig (assembly);

			// User config (provided through a settings.conf file)
			var execution_path = AppDomain.CurrentDomain.BaseDirectory;
			var user_config_filename = "settings";

			// TODO this is ugly but makes life easier
			// TODO not windows compatible
			if (execution_path.EndsWith ("/bin/Debug/")) {
				// we are run from the IDE, so the settings.conf needs
				// to be searched two levels up
				execution_path = execution_path.Replace ("/bin/Debug", "");
			}

			var d = new DirectoryInfo (execution_path);
			var userConfig = (from FileInfo fi in d.GetFiles ()
				where (
					fi.FullName.EndsWith (user_config_filename + ".conf") ||
					fi.FullName.EndsWith (user_config_filename + ".json") ||
					fi.FullName.EndsWith (user_config_filename + ".conf.json") ||
					fi.FullName.EndsWith (user_config_filename + ".json.conf")
				) select fi).FirstOrDefault ();

			if (userConfig != null) {
				User = Config.ParseJson (File.ReadAllText (userConfig.FullName));
				WatchUserConfig (userConfig);
			}
			else {
				User = new NullExceptionPreventer ();
			}
		}
		private static FileSystemWatcher userConfigWatcher;
		public static void WatchUserConfig (FileInfo info)
		{
			userConfigWatcher = new FileSystemWatcher (info.Directory.FullName, info.Name);
			userConfigWatcher.NotifyFilter = NotifyFilters.LastWrite;
			userConfigWatcher.Changed += delegate {
				User = (ConfigObject) ParseJson (File.ReadAllText (info.FullName));
				Console.WriteLine ("user configuration has changed, updating config information");

				// invalidate the Global config, forcing a re-merge next time its accessed
				global_config = null;

				// trigger our event
				if (OnUserConfigFileChanged != null)
					OnUserConfigFileChanged ();
			};
			userConfigWatcher.EnableRaisingEvents = true;
		}
		public static ConfigObject ApplyJsonFromFileInfo (FileInfo file, ConfigObject config = null)
		{
			var overlay_json = File.ReadAllText (file.FullName);
			dynamic overlay_config = ParseJson (overlay_json);
			return Merger.Merge (overlay_config, config);
		}
		public static ConfigObject ApplyJsonFromPath (string path, ConfigObject config = null)
		{
			return ApplyJsonFromFileInfo (new FileInfo (path), config);
		}
		public static ConfigObject ApplyJson (string json, ConfigObject config = null)
		{
			if (config == null)
				config = new ConfigObject ();

			dynamic parsed = ParseJson (json);
			return Merger.Merge (parsed, config);
		}
		// seeks a folder for .conf files
		public static ConfigObject ApplyFromDirectory (string path, ConfigObject config = null, bool recursive = false)
		{
			if (!Directory.Exists (path))
				throw new Exception ("no folder found in the given path");

			if (config == null)
				config = new ConfigObject ();

			DirectoryInfo info = new DirectoryInfo (path);
			if (recursive) {
				foreach (var dir in info.GetDirectories ()) {
					Console.WriteLine ("reading in folder {0}", dir.ToString ());
					config = ApplyFromDirectoryInfo (dir, config, recursive);
				}
			}

			// find all files
			var files = info.GetFiles ();
			foreach (var file in files) {
				Console.WriteLine ("reading in file {0}", file.ToString ());
				config = ApplyJsonFromFileInfo (file, config);
			}
			return config;
		}
		public static ConfigObject ApplyFromDirectoryInfo (DirectoryInfo info, ConfigObject config = null, bool recursive = false)
		{
			return ApplyFromDirectory (info.FullName, config, recursive);
		}

		public static ConfigObject ParseJson (string json)
		{
			var lines = json.Split (new char[] {'\n'});
			// remove lines that start with a dash # character 
			var filtered = from l in lines
				where !(Regex.IsMatch (l, @"^\s*#(.*)"))
				select l;
			
			var filtered_json = string.Join ("\n", filtered);
			
			var json_reader = new JsonReader ();
			dynamic parsed = json_reader.Read (filtered_json);
			// convert the ExpandoObject to ConfigObject before returning
			return ConfigObject.FromExpando (parsed);
		}
		// overrides any default config specified in default.conf
		public static void SetDefaultConfig (dynamic config)
		{
			Default = config;
		}
		public static void SetUserConfig (ConfigObject config)
		{
			User = config;
			// disable the watcher
			if (userConfigWatcher != null) {
				userConfigWatcher.EnableRaisingEvents = false;
				userConfigWatcher.Dispose ();
				userConfigWatcher = null;
			}
		}
		private static dynamic GetDefaultConfig (Assembly assembly)
		{
			var dconf_json = ScanForDefaultConfig (assembly);
			if (dconf_json == null)
				return null;
			return ParseJson (dconf_json);
		}
		private static string ScanForDefaultConfig(Assembly assembly)
		{
			if(assembly == null)
				assembly = System.Reflection.Assembly.GetEntryAssembly ();
			
			string[] res = assembly.GetManifestResourceNames ();
			
			var dconf_resource = res.Where (r =>
					r.EndsWith ("default.conf", StringComparison.OrdinalIgnoreCase) ||
					r.EndsWith ("default.json", StringComparison.OrdinalIgnoreCase) ||
					r.EndsWith ("default.conf.json", StringComparison.OrdinalIgnoreCase))
				.FirstOrDefault ();
			
			if(string.IsNullOrEmpty (dconf_resource))
				return null;
		
			var stream = assembly.GetManifestResourceStream (dconf_resource);
			string default_json = new StreamReader(stream).ReadToEnd ();
			return default_json;
		}
	}
}
