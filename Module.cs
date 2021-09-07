﻿using Blish_HUD;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Blish_HUD.Controls;
using static Blish_HUD.GameService;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using Manlaan.Dailies.Models;
using Manlaan.Dailies.Controls;

namespace Manlaan.Dailies
{
    [Export(typeof(Blish_HUD.Modules.Module))]
    public class Module : Blish_HUD.Modules.Module
    {

        private static readonly Logger Logger = Logger.GetLogger<Module>();
        internal static Module ModuleInstance;

        public static List<Daily> _dailies = new List<Daily>();
        public static List<Daily> _newdailies = new List<Daily>();
        public static List<Category> _categories = new List<Category>();
        public static SettingEntry<DateTime> _settingLastReset;
        //private SettingEntry<string> _settingFestivalStart;
        private SettingEntry<Point> _settingMiniLocation;
        private SettingEntry<string> _settingMiniSizeW, _settingMiniSizeH;
        private Stopwatch Timer = new Stopwatch();
        public static DailySettings _dailySettings;
        private Texture2D _pageIcon;

        private MainWindow _mainWindow;
        private MiniWindow _miniWindow;
        private CornerIcon _cornerIcon;

        private WindowTab _moduleTab;


        #region Service Managers
        internal SettingsManager SettingsManager => this.ModuleParameters.SettingsManager;
        internal ContentsManager ContentsManager => this.ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => this.ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager Gw2ApiManager => this.ModuleParameters.Gw2ApiManager;
        #endregion
        internal static Module Instance { get; private set; }

        [ImportingConstructor]
        public Module([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { ModuleInstance = this; }

        protected override void DefineSettings(SettingCollection settings) {
            //_settingFestivalStart = settings.DefineSetting("DailyFestivalStart", @"01/01/2000", "Festival Start Date", "");
            _settingLastReset = settings.DefineSetting("DailyLastUpdate", new DateTime());
            _settingMiniLocation = settings.DefineSetting("DailyMiniLoc", new Point(100, 100));
            _settingMiniSizeW = settings.DefineSetting("DailyMiniSizeH", @"280", "Mini Window Width", "");
            _settingMiniSizeH = settings.DefineSetting("DailyMiniSizeW", @"450", "Mini Window Height", "");
            _settingMiniSizeH.SettingChanged += UpdateSettings;
            _settingMiniSizeW.SettingChanged += UpdateSettings;

            //_dailiesTracked = settings.AddSubCollection("Tracked");
            //_dailiesComplete = settings.AddSubCollection("Complete");
            //var selfManagedSettings = settings.AddSubCollection("Managed Settings");
        }
        private void UpdateSettings(object sender = null, ValueChangedEventArgs<string> e = null) {
            if (int.Parse(_settingMiniSizeH.Value) < 0)
                _settingMiniSizeH.Value = "0";
            if (int.Parse(_settingMiniSizeW.Value) < 0)
                _settingMiniSizeW.Value = "0";

            _miniWindow.Dispose();
            _miniWindow = new MiniWindow(new Point(int.Parse(_settingMiniSizeW.Value), int.Parse(_settingMiniSizeH.Value))) {
                Location = _settingMiniLocation.Value,
                Parent = GameService.Graphics.SpriteScreen
            };
            foreach (Daily d in _dailies) {
                d.MiniButton = _miniWindow.CreateDailyButton(d);
            }
            UpdateDailyPanel();
        }

        protected override void Initialize() {
            _pageIcon = ContentsManager.GetTexture("42684bw.png");

            _dailySettings = new DailySettings(DirectoriesManager.GetFullDirectoryPath("dailies"));
            _dailySettings.LoadSettings();
            if (_dailySettings._dailySettings.Count == 0)
                _dailySettings.SetTracked("gather_home", true);

            ExtractFile("sample.txt");

            string[] jsonfiles = {
                "Daily.json",
                "Adventures.json",
                "BjoraMarches.json",
                "Champions.json",
                "Converters.json",
                "Crafting.json",
                "Drizzlewood.json",
                "Dungeons.json",
                "Fractals.json",
                "Gathering.json",
                "GrothmarValley.json",
                "JumpingPuzzles.json",
                "LivingWorld.json",
                "Merchants.json",
                "Raids.json",
                "StrikeMissions.json",
                "WorldBosses.json",

                "DragonBash.json",
                "FourWinds.json",
                "Halloween.json",
                "LunarNewYear.json",
                "SuperAdventure.json",
                "Wintersday.json",
            };

            foreach (string file in jsonfiles) {
                //Logger.Info("Loading Daily File: " + file);
                _dailies.AddRange(readJson(ContentsManager.GetFileStream(file), file));
            }
            string dailiesDirectory = DirectoriesManager.GetFullDirectoryPath("dailies");
            foreach (string file in Directory.GetFiles(dailiesDirectory, ".")) {
                if (file.ToLower().Contains(".json") && !file.ToLower().Contains("settings.json")) {
                    //Logger.Info("Loading Daily File: " + file);
                    if (file.ToLower().Contains("new.json")) {
                        List<Daily> newdaily = readJson(new FileStream(file, FileMode.Open, FileAccess.Read), file);
                        foreach (Daily d in newdaily) {
                            if (_dailies.Find(x => x.Achievement.Equals(d.Achievement)) == null) {
                                _dailies.Add(d);
                                _newdailies.Add(d);
                            }
                        }
                    }
                    else {
                        _dailies.AddRange(readJson(new FileStream(file, FileMode.Open, FileAccess.Read), file));
                    }
                }
            }
            var options = new JsonSerializerOptions { WriteIndented = true };
            FileStream createStream = File.Create(DirectoriesManager.GetFullDirectoryPath("dailies") + "\\new.json");
            JsonSerializer.SerializeAsync(createStream, _newdailies, options);

            foreach (Daily d in _dailies) {
                DailySettingEntry daySetting = _dailySettings.Get(d.Id);
                d.IsComplete = daySetting.IsComplete;
                d.IsTracked = daySetting.IsTracked;
                if (!_categories.Exists(x => x.Name.Equals(d.Category)))
                    _categories.Add(new Category() { Name = d.Category, IsActive = false });
            }

            _categories.Sort(delegate (Category x, Category y) {
                if (x.Name == null && y.Name == null) return 0;
                else if (x.Name == null) return -1;
                else if (y.Name == null) return 1;
                else return x.Name.CompareTo(y.Name);
            });
            _dailies.Sort(delegate (Daily x, Daily y) {
                if (x.Name == null && y.Name == null) return 0;
                else if (x.Name == null) return -1;
                else if (y.Name == null) return 1;
                else return x.Name.CompareTo(y.Name);
            });

            _mainWindow = new MainWindow(Overlay.BlishHudWindow.ContentRegion.Size);
            _miniWindow = new MiniWindow(new Point(int.Parse(_settingMiniSizeW.Value), int.Parse(_settingMiniSizeH.Value))) {
                Location = _settingMiniLocation.Value,
                Parent = GameService.Graphics.SpriteScreen,
            };
            foreach (Daily d in _dailies) {
                d.Button = _mainWindow.CreateDailyButton(d);
                d.MiniButton = _miniWindow.CreateDailyButton(d);
            }

            _cornerIcon = new CornerIcon() {
                IconName = "Dailies",
                Icon = _pageIcon,
                HoverIcon = _pageIcon,
                Priority = 10
            };
            _cornerIcon.Click += delegate { _miniWindow.ToggleWindow(); };
        }
        protected override async Task LoadAsync() {
        }

        protected override void OnModuleLoaded(EventArgs e) {
            Timer.Start();
            _moduleTab = Overlay.BlishHudWindow.AddTab("Dailies", _pageIcon, _mainWindow._parentPanel);
            UpdateAchievements();

            // Base handler must be called
            base.OnModuleLoaded(e);
        }

        internal void UpdateDailyPanel() {
            _dailySettings.SaveSettings();
            _miniWindow.UpdateDailyPanel();
            _mainWindow.UpdateDailyPanel();
        }
        internal async void UpdateAchievements() {
            Timer.Restart();
            List<string> TodayAchieve = new List<string>();
            List<Achievement> AllAchieves = new List<Achievement>();
            bool newDaily = false;

            try {
                var apiAchieveDay = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.Daily.GetAsync();
                foreach (var a in apiAchieveDay.Fractals)
                    if (a.Level.Max == 80) {
                        TodayAchieve.Add(a.Id.ToString());
                    }
                foreach (var a in apiAchieveDay.Pve)
                    if (a.Level.Max == 80)
                        TodayAchieve.Add(a.Id.ToString());
                foreach (var a in apiAchieveDay.Pvp)
                    if (a.Level.Max == 80)
                        TodayAchieve.Add(a.Id.ToString());
                foreach (var a in apiAchieveDay.Special)
                    if (a.Level.Max == 80)
                        TodayAchieve.Add(a.Id.ToString());
                foreach (var a in apiAchieveDay.Wvw)
                    if (a.Level.Max == 80)
                        TodayAchieve.Add(a.Id.ToString());

                var apiGroup = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.Groups.GetAsync(new Guid("18DB115A-8637-4290-A636-821362A3C4A8"));
                foreach (int grp in apiGroup.Categories) {
                    if (grp != 97) {
                        var apidata = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.Categories.GetAsync(grp);
                        foreach (var data in apidata.Achievements) {
                            TodayAchieve.Add(data.ToString());
                        }
                    }
                }

                foreach (string ach in TodayAchieve) {
                    Daily daily = _dailies.Find(x => x.Achievement.Equals(ach));
                    if (daily == null) {
                        newDaily = true;
                        var apidata = await Gw2ApiManager.Gw2ApiClient.V2.Achievements.GetAsync(Int32.Parse(ach));

                        _newdailies.Add(new Daily { Id = "new_" + ach, Name = apidata.Name, Category = "New", Achievement = ach, API = "achievements", Note = apidata.Description });

                        _dailies.Add(new Daily { Id = "new_" + ach, Name = apidata.Name, Category = "New", Achievement = ach, API = "achievements", Note = apidata.Description });
                        daily = _dailies.Find(x => x.Achievement.Equals(ach));
                        daily.Button = _mainWindow.CreateDailyButton(daily);
                        daily.MiniButton = _miniWindow.CreateDailyButton(daily);

                        if (!_categories.Exists(x => x.Name.Equals("New")))
                            _categories.Add(new Category() { Name = "New", IsActive = false });
                    }
                }
            }
            catch { }


            TodayAchieve.AddRange(DailyList.Activity.Dailies());
            TodayAchieve.AddRange(DailyList.Merchants.Dailies());
            //TodayAchieve.AddRange(DailyList.StrikeMissions.Dailies());  //For Weekly - Broken due to daily reset
            //TodayAchieve.AddRange(DailyList.DragonBash.Dailies(_settingFestivalStart.Value)); //Maybe part of daily api 
            //TodayAchieve.AddRange(DailyList.FourWinds.Dailies(_settingFestivalStart.Value));  //Not needed. Part of Daily API
            //TodayAchieve.AddRange(DailyList.Halloween.Dailies(_settingFestivalStart.Value));  //Maybe part of daily api
            //TodayAchieve.AddRange(DailyList.LunarNewYear.Dailies(_settingFestivalStart.Value));  //Maybe part of daily api
            //TodayAchieve.AddRange(DailyList.Wintersday.Dailies(_settingFestivalStart.Value));  //Maybe part of daily api


            if (Gw2ApiManager.HavePermissions(new[] { Gw2Sharp.WebApi.V2.Models.TokenPermission.Account, Gw2Sharp.WebApi.V2.Models.TokenPermission.Progression })) {
                try {
                    var apiDungeon = await Gw2ApiManager.Gw2ApiClient.V2.Account.Dungeons.GetAsync();
                    foreach (var a in apiDungeon) {
                        AllAchieves.Add(new Achievement { Id = a, Done = true, API = "dungeons" });
                    }
                    var apiChests = await Gw2ApiManager.Gw2ApiClient.V2.Account.MapChests.GetAsync();
                    foreach (var a in apiChests) {
                        AllAchieves.Add(new Achievement { Id = a, Done = true, API = "mapchests" });
                    }
                    var apiRaids = await Gw2ApiManager.Gw2ApiClient.V2.Account.Raids.GetAsync();
                    foreach (var a in apiRaids) {
                        AllAchieves.Add(new Achievement { Id = a, Done = true, API = "raids" });
                    }
                    var apiBosses = await Gw2ApiManager.Gw2ApiClient.V2.Account.WorldBosses.GetAsync();
                    foreach (var a in apiBosses) {
                        AllAchieves.Add(new Achievement { Id = a, Done = true, API = "worldbosses" });
                    }
                    var apiCraft = await Gw2ApiManager.Gw2ApiClient.V2.Account.DailyCrafting.GetAsync();
                    foreach (var a in apiCraft) {
                        AllAchieves.Add(new Achievement { Id = a, Done = true, API = "dailycrafting" });
                    }

                    foreach (Achievement ach in AllAchieves) {
                        Daily daily = _dailies.Find(x => x.Achievement.Equals(ach.Id));
                        if (daily == null) {
                            newDaily = true;
                            _newdailies.Add(new Daily { Id = "new_" + ach.Id, Name = ach.Id, Category = "New", Achievement = ach.Id, API = ach.API, Note = ach.API});

                            _dailies.Add(new Daily { Id = "new_" + ach.Id, Name = ach.Id, Category = "New", Achievement = ach.Id, API = ach.API, Note = ach.API });
                            daily = _dailies.Find(x => x.Achievement.Equals(ach.Id));
                            daily.Button = _mainWindow.CreateDailyButton(daily);
                            daily.MiniButton = _miniWindow.CreateDailyButton(daily);

                            if (!_categories.Exists(x => x.Name.Equals("New")))
                                _categories.Add(new Category() { Name = "New", IsActive = false });
                        }
                    }
                }
                catch { }
            }

            foreach (Daily btn in _dailies) {
                if (!string.IsNullOrEmpty(btn.Achievement) && !string.IsNullOrEmpty(btn.API)) {
                    switch (btn.API) {
                        default:
                            btn.IsDaily = TodayAchieve.Contains(btn.Achievement) ? true : false;
                            break;
                        //Autocompletes
                        case "dungeons":
                        case "mapchests":
                        case "raids":
                        case "worldbosses":
                        case "dailycrafting":
                            Achievement ach = AllAchieves.Find(x => x.Id.Equals(btn.Achievement));
                            bool complete = (ach == null) ? false : ach.Done;

                            _dailySettings.SetComplete(btn.Id, complete);

                            btn.IsComplete = complete;
                            btn.Button.CompleteButton.Checked = complete;
                            btn.MiniButton.CompleteButton.Checked = complete;
                            btn.IsDaily = true;
                            break;
                    }
                }
                else {
                    btn.IsDaily = true;
                }
            }
            if (newDaily) {
                var options = new JsonSerializerOptions { WriteIndented = true };
                FileStream createStream = File.Create(DirectoriesManager.GetFullDirectoryPath("dailies") + "\\new.json");
                await JsonSerializer.SerializeAsync(createStream, _newdailies, options);
            }
            UpdateDailyPanel();
        }

        protected override void Update(GameTime gameTime) {
            if (Timer.ElapsedMilliseconds > 120000) {   //2 minutes  (2min * 60sec * 1000ms)
                UpdateAchievements();
            }
            if (_settingLastReset.Value <= DateTime.UtcNow.Date.AddDays(-1)) {
                _settingLastReset.Value = DateTime.UtcNow.Date;
                _mainWindow.SetAllComplete(false, true);
                UpdateAchievements();
            }
            if (_miniWindow.Location != _settingMiniLocation.Value)
                _settingMiniLocation.Value = _miniWindow.Location;
        }

        /// <inheritdoc />
        protected override void Unload() {
            // Unload here
            Overlay.BlishHudWindow.RemoveTab(_moduleTab);
            _settingMiniSizeH.SettingChanged -= UpdateSettings;
            _settingMiniSizeW.SettingChanged -= UpdateSettings;

            _mainWindow?.Dispose();
            _miniWindow?.Dispose();
            _cornerIcon?.Dispose();
            // All static members must be manually unset
            ModuleInstance = null;
        }


        private List<Daily> readJson(Stream fileStream, string filename) {
            List<Daily> data = new List<Daily>();
            JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                IgnoreNullValues = true
            };

            string jsonContent;
            using (var jsonReader = new StreamReader(fileStream)) {
                jsonContent = jsonReader.ReadToEnd();
            }

            try {
                data = JsonSerializer.Deserialize<List<Daily>>(jsonContent, jsonOptions);
                Logger.Info("Loaded File: " + filename);
            }
            catch (Exception ex) {
                Logger.Error("Failed Deserialization: " + filename);
            }
            return data;
        }
        private void ExtractFile(string filePath) {
            var fullPath = Path.Combine(DirectoriesManager.GetFullDirectoryPath("dailies"), filePath);
            //if (File.Exists(fullPath)) return;
            using (var fs = ContentsManager.GetFileStream(filePath)) {
                fs.Position = 0;
                byte[] buffer = new byte[fs.Length];
                var content = fs.Read(buffer, 0, (int)fs.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllBytes(fullPath, buffer);
            }
        }
        public static bool UrlIsValid(string source) => Uri.TryCreate(source, UriKind.Absolute, out Uri uriResult) && uriResult.Scheme == Uri.UriSchemeHttps;

        public static bool InSection(Daily d, string section, string search, string category) {
            switch (section) {
                case "Tracked - Incomplete":
                    if (d.IsTracked && !d.IsComplete && d.Name.ToLower().Contains(search.ToLower()) && (d.Category.Equals(category) || category.Equals("")) && d.IsDaily)
                        return true;
                    break;
                case "Tracked - Complete":
                    if (d.IsTracked && d.IsComplete && d.Name.ToLower().Contains(search.ToLower()) && (d.Category.Equals(category) || category.Equals("")) && d.IsDaily)
                        return true;
                    break;
                case "Tracked - All Daily":
                    if (d.IsTracked && d.Name.ToLower().Contains(search.ToLower()) && (d.Category.Equals(category) || category.Equals("")) && d.IsDaily)
                        return true;
                    break;
                case "Tracked - Not Daily":
                    if (d.IsTracked && d.Name.ToLower().Contains(search.ToLower()) && (d.Category.Equals(category) || category.Equals("")) && !d.IsDaily)
                        return true;
                    break;
                case "Tracked - All":
                    if (d.IsTracked && d.Name.ToLower().Contains(search.ToLower()) && (d.Category.Equals(category) || category.Equals("")))
                        return true;
                    break;
                case "Untracked":
                    if (!d.IsTracked && d.Name.ToLower().Contains(search.ToLower()) && (d.Category.Equals(category) || category.Equals("")))
                        return true;
                    break;
                default:
                    if (d.Name.ToLower().Contains(search.ToLower()) && (d.Category.Equals(category) || category.Equals("")))
                        return true;
                    break;
            }
            return false;
        }
    }
}
