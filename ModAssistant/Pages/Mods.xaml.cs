using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Navigation;
using System.IO.Compression;
using System.Diagnostics;
using System.Windows.Forms;
using static ModAssistant.Http;

namespace ModAssistant.Pages
{
    /// <summary>
    /// Interaction logic for Mods.xaml
    /// </summary>
    public sealed partial class Mods : Page
    {
        public static Mods Instance = new Mods();

        public List<string> DefaultMods = new List<string>() { "SongCore", "ScoreSaber", "BeatSaverDownloader", "BeatSaverVoting", "PlaylistCore", "Survey" };
        public Mod[] ModsList;
        public Mod[] AllModsList;
        public static List<Mod> InstalledMods = new List<Mod>();
        public List<string> CategoryNames = new List<string>();
        public CollectionView view;
        public bool PendingChanges;

        private readonly SemaphoreSlim _modsLoadSem = new SemaphoreSlim(1, 1);

        public List<ModListItem> ModList { get; set; }

        public Mods()
        {
            InitializeComponent();
        }

        private void RefreshModsList()
        {
            if (view != null)
            {
                view.Refresh();
            }
        }

        public async Task LoadMods()
        {
            var versionLoadSuccess = await MainWindow.Instance.VersionLoadStatus.Task;
            if (versionLoadSuccess == false) return;

            await _modsLoadSem.WaitAsync();

            try
            {
                MainWindow.Instance.InstallButton.IsEnabled = false;
                MainWindow.Instance.GameVersionsBox.IsEnabled = false;
                MainWindow.Instance.InfoButton.IsEnabled = false;

                if (ModsList != null)
                {
                    Array.Clear(ModsList, 0, ModsList.Length);
                }

                if (AllModsList != null)
                {
                    Array.Clear(AllModsList, 0, AllModsList.Length);
                }

                InstalledMods = new List<Mod>();
                CategoryNames = new List<string>();
                ModList = new List<ModListItem>();

                ModsListView.Visibility = Visibility.Hidden;

                if (App.CheckInstalledMods)
                {
                    MainWindow.Instance.MainText = $"{FindResource("Mods:CheckingInstalledMods")}...";
                    await Task.Run(async () => await CheckInstalledMods());
                    InstalledColumn.Width = double.NaN;
                    UninstallColumn.Width = 70;
                    DescriptionColumn.Width = 750;
                }
                else
                {
                    InstalledColumn.Width = 0;
                    UninstallColumn.Width = 0;
                    DescriptionColumn.Width = 800;
                }

                MainWindow.Instance.MainText = $"{FindResource("Mods:LoadingMods")}...";
                await Task.Run(async () => await PopulateModsList());

                ModsListView.ItemsSource = ModList;

                view = (CollectionView)CollectionViewSource.GetDefaultView(ModsListView.ItemsSource);
                PropertyGroupDescription groupDescription = new PropertyGroupDescription("Category");
                view.GroupDescriptions.Add(groupDescription);

                this.DataContext = this;

                RefreshModsList();
                ModsListView.Visibility = Visibility.Visible;
                MainWindow.Instance.MainText = $"{FindResource("Mods:FinishedLoadingMods")}.";

                MainWindow.Instance.InstallButton.IsEnabled = true;
                MainWindow.Instance.GameVersionsBox.IsEnabled = true;
            }
            finally
            {
                _modsLoadSem.Release();
            }
        }

        public async Task CheckInstalledMods()
        {
            await GetAllMods();
            List<string> empty = new List<string>();
            GetBSIPAVersion();
            CheckInstallDir("IPA/Pending/Plugins", empty);
            CheckInstallDir("IPA/Pending/Libs", empty);
            CheckInstallDir("Plugins", empty);
            CheckInstallDir("Libs", empty);
        }

        public async Task GetAllMods()
        {
            var resp = await HttpClient.GetAsync(Utils.Constants.BeatModsAPIUrl + "mod");
            var body = await resp.Content.ReadAsStringAsync();
            AllModsList = JsonSerializer.Deserialize<Mod[]>(body);
        }

        private void CheckInstallDir(string directory, List<string> blacklist)
        {
            if (!Directory.Exists(Path.Combine(App.BeatSaberInstallDirectory, directory)))
            {
                return;
            }

            foreach (string file in Directory.GetFileSystemEntries(Path.Combine(App.BeatSaberInstallDirectory, directory)))
            {
                if (File.Exists(file) && Path.GetExtension(file) == ".dll" || Path.GetExtension(file) == ".manifest")
                {
                    Mod mod = GetModFromHash(Utils.CalculateMD5(file));
                    if (mod != null)
                    {
                        AddDetectedMod(mod);
                    }
                }
            }
        }

        public void GetBSIPAVersion()
        {
            string InjectorPath = Path.Combine(App.BeatSaberInstallDirectory, "Beat Saber_Data", "Managed", "IPA.Injector.dll");
            if (!File.Exists(InjectorPath)) return;

            string InjectorHash = Utils.CalculateMD5(InjectorPath);
            foreach (Mod mod in AllModsList)
            {
                if (mod.name.ToLower() == "bsipa")
                {
                    foreach (Mod.DownloadLink download in mod.downloads)
                    {
                        foreach (Mod.FileHashes fileHash in download.hashMd5)
                        {
                            if (fileHash.hash == InjectorHash)
                            {
                                AddDetectedMod(mod);
                            }
                        }
                    }
                }
            }
        }

        private void AddDetectedMod(Mod mod)
        {
            if (!InstalledMods.Contains(mod))
            {
                InstalledMods.Add(mod);
                if (App.SelectInstalledMods && !DefaultMods.Contains(mod.name))
                {
                    DefaultMods.Add(mod.name);
                }
            }
        }

        private Mod GetModFromHash(string hash)
        {
            foreach (Mod mod in AllModsList)
            {
                if (mod.name.ToLower() != "bsipa")
                {
                    foreach (Mod.DownloadLink download in mod.downloads)
                    {
                        foreach (Mod.FileHashes fileHash in download.hashMd5)
                        {
                            if (fileHash.hash == hash)
                                return mod;
                        }
                    }
                }
            }

            return null;
        }

        public async Task PopulateModsList()
        {
            try
            {
                var resp = await HttpClient.GetAsync(Utils.Constants.BeatModsAPIUrl + Utils.Constants.BeatModsModsOptions + "&gameVersion=" + MainWindow.GameVersion);
                var body = await resp.Content.ReadAsStringAsync();
                ModsList = JsonSerializer.Deserialize<Mod[]>(body);
            }
            catch (Exception e)
            {
                System.Windows.MessageBox.Show($"{FindResource("Mods:LoadFailed")}.\n\n" + e);
                return;
            }

            foreach (Mod mod in ModsList)
            {
                bool preSelected = mod.required;
                if (DefaultMods.Contains(mod.name) || (App.SaveModSelection && App.SavedMods.Contains(mod.name)))
                {
                    preSelected = true;
                    if (!App.SavedMods.Contains(mod.name))
                    {
                        App.SavedMods.Add(mod.name);
                    }
                }

                RegisterDependencies(mod);

                ModListItem ListItem = new ModListItem()
                {
                    IsSelected = preSelected,
                    IsEnabled = !mod.required,
                    ModName = mod.name,
                    ModVersion = mod.version,
                    ModDescription = mod.description.Replace("\r\n", " ").Replace("\n", " "),
                    ModInfo = mod,
                    Category = mod.category
                };

                foreach (Promotion promo in Promotions.ActivePromotions)
                {
                    if (mod.name == promo.ModName)
                    {
                        ListItem.PromotionText = promo.Text;
                        ListItem.PromotionLink = promo.Link;
                    }
                }

                foreach (Mod installedMod in InstalledMods)
                {
                    if (mod.name == installedMod.name)
                    {
                        ListItem.InstalledModInfo = installedMod;
                        ListItem.IsInstalled = true;
                        ListItem.InstalledVersion = installedMod.version;
                        break;
                    }
                }

                mod.ListItem = ListItem;

                ModList.Add(ListItem);
            }

            foreach (Mod mod in ModsList)
            {
                ResolveDependencies(mod);
            }
        }

        public async void InstallMods()
        {
            MainWindow.Instance.InstallButton.IsEnabled = false;
            string installDirectory = App.BeatSaberInstallDirectory;

            foreach (Mod mod in ModsList)
            {
                if (mod.name.ToLower() == "bsipa")
                {
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstallingMod"), mod.name)}...";
                    await Task.Run(async () => await InstallMod(mod, installDirectory));
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstalledMod"), mod.name)}.";
                    if (!File.Exists(Path.Combine(installDirectory, "winhttp.dll")))
                    {
                        await Task.Run(() =>
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = Path.Combine(installDirectory, "IPA.exe"),
                                WorkingDirectory = installDirectory,
                                Arguments = "-n"
                            }).WaitForExit()
                        );
                    }

                    Pages.Options.Instance.YeetBSIPA.IsEnabled = true;
                }
                else if (mod.ListItem.IsSelected)
                {
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstallingMod"), mod.name)}...";
                    await Task.Run(async () => await InstallMod(mod, Path.Combine(installDirectory, @"IPA\Pending")));
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstalledMod"), mod.name)}.";
                }
            }

            MainWindow.Instance.MainText = $"{FindResource("Mods:FinishedInstallingMods")}.";
            MainWindow.Instance.InstallButton.IsEnabled = true;
            RefreshModsList();
        }

        private async Task InstallMod(Mod mod, string directory)
        {
            string downloadLink = null;

            foreach (Mod.DownloadLink link in mod.downloads)
            {
                if (link.type == "universal")
                {
                    downloadLink = link.url;
                    break;
                }
                else if (link.type.ToLower() == App.BeatSaberInstallType.ToLower())
                {
                    downloadLink = link.url;
                    break;
                }
            }

            if (string.IsNullOrEmpty(downloadLink))
            {
                System.Windows.MessageBox.Show(string.Format((string)FindResource("Mods:ModDownloadLinkMissing"), mod.name));
                return;
            }

            using (Stream stream = await DownloadMod(Utils.Constants.BeatModsURL + downloadLink))
            using (ZipArchive archive = new ZipArchive(stream))
            {
                foreach (ZipArchiveEntry file in archive.Entries)
                {
                    string fileDirectory = Path.GetDirectoryName(Path.Combine(directory, file.FullName));
                    if (!Directory.Exists(fileDirectory))
                    {
                        Directory.CreateDirectory(fileDirectory);
                    }

                    if (!string.IsNullOrEmpty(file.Name))
                    {
                        file.ExtractToFile(Path.Combine(directory, file.FullName), true);
                    }
                }
            }

            if (App.CheckInstalledMods)
            {
                mod.ListItem.IsInstalled = true;
                mod.ListItem.InstalledVersion = mod.version;
                mod.ListItem.InstalledModInfo = mod;
            }
        }

        private async Task<Stream> DownloadMod(string link)
        {
            var resp = await HttpClient.GetAsync(link);
            return await resp.Content.ReadAsStreamAsync();
        }

        private void RegisterDependencies(Mod dependent)
        {
            if (dependent.dependencies.Length == 0)
                return;

            foreach (Mod mod in ModsList)
            {
                foreach (Mod.Dependency dep in dependent.dependencies)
                {

                    if (dep.name == mod.name)
                    {
                        dep.Mod = mod;
                        mod.Dependents.Add(dependent);

                    }
                }
            }
        }

        private void ResolveDependencies(Mod dependent)
        {
            if (dependent.ListItem.IsSelected && dependent.dependencies.Length > 0)
            {
                foreach (Mod.Dependency dependency in dependent.dependencies)
                {
                    if (dependency.Mod.ListItem.IsEnabled)
                    {
                        dependency.Mod.ListItem.PreviousState = dependency.Mod.ListItem.IsSelected;
                        dependency.Mod.ListItem.IsSelected = true;
                        dependency.Mod.ListItem.IsEnabled = false;
                        ResolveDependencies(dependency.Mod);
                    }
                }
            }
        }

        private void UnresolveDependencies(Mod dependent)
        {
            if (!dependent.ListItem.IsSelected && dependent.dependencies.Length > 0)
            {
                foreach (Mod.Dependency dependency in dependent.dependencies)
                {
                    if (!dependency.Mod.ListItem.IsEnabled)
                    {
                        bool needed = false;
                        foreach (Mod dep in dependency.Mod.Dependents)
                        {
                            if (dep.ListItem.IsSelected)
                            {
                                needed = true;
                                break;
                            }
                        }
                        if (!needed && !dependency.Mod.required)
                        {
                            dependency.Mod.ListItem.IsSelected = dependency.Mod.ListItem.PreviousState;
                            dependency.Mod.ListItem.IsEnabled = true;
                            UnresolveDependencies(dependency.Mod);
                        }
                    }
                }
            }
        }

        private void ModCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Mod mod = ((sender as System.Windows.Controls.CheckBox).Tag as Mod);
            mod.ListItem.IsSelected = true;
            ResolveDependencies(mod);
            App.SavedMods.Add(mod.name);
            Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
            Properties.Settings.Default.Save();
            RefreshModsList();
        }

        private void ModCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            Mod mod = ((sender as System.Windows.Controls.CheckBox).Tag as Mod);
            mod.ListItem.IsSelected = false;
            UnresolveDependencies(mod);
            App.SavedMods.Remove(mod.name);
            Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
            Properties.Settings.Default.Save();
            RefreshModsList();
        }

        public class Category
        {
            public string CategoryName { get; set; }
            public List<ModListItem> Mods = new List<ModListItem>();
        }

        public class ModListItem
        {
            public string ModName { get; set; }
            public string ModVersion { get; set; }
            public string ModDescription { get; set; }
            public bool PreviousState { get; set; }

            public bool IsEnabled { get; set; }
            public bool IsSelected { get; set; }
            public Mod ModInfo { get; set; }
            public string Category { get; set; }

            public Mod InstalledModInfo { get; set; }
            public bool IsInstalled { get; set; }
            private string _installedVersion { get; set; }
            public string InstalledVersion
            {
                get
                {
                    return (string.IsNullOrEmpty(_installedVersion) || !IsInstalled) ? "-" : _installedVersion;
                }
                set
                {
                    _installedVersion = value;
                }
            }

            public string GetVersionColor
            {
                get
                {
                    if (!IsInstalled) return "Black";
                    return InstalledVersion == ModVersion ? "Green" : "Red";
                }
            }

            public string GetVersionDecoration
            {
                get
                {
                    if (!IsInstalled) return "None";
                    return InstalledVersion == ModVersion ? "None" : "Strikethrough";
                }
            }

            public bool CanDelete
            {
                get
                {
                    return (!ModInfo.required && IsInstalled);
                }
            }

            public string CanSeeDelete
            {
                get
                {
                    if (!ModInfo.required && IsInstalled)
                        return "Visible";
                    else
                        return "Hidden";
                }
            }

            public string PromotionText { get; set; }
            public string PromotionLink { get; set; }
            public string PromotionMargin
            {
                get
                {
                    if (string.IsNullOrEmpty(PromotionText)) return "0";
                    return "0,0,5,0";
                }
            }
        }

        private void ModsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((Mods.ModListItem)Mods.Instance.ModsListView.SelectedItem == null)
            {
                MainWindow.Instance.InfoButton.IsEnabled = false;
            }
            else
            {
                MainWindow.Instance.InfoButton.IsEnabled = true;
            }
        }

        public void UninstallBSIPA(Mod.DownloadLink links)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(App.BeatSaberInstallDirectory, "IPA.exe"),
                WorkingDirectory = App.BeatSaberInstallDirectory,
                Arguments = "--revert -n"
            }).WaitForExit();

            foreach (Mod.FileHashes files in links.hashMd5)
            {
                string file = files.file.Replace("IPA/", "").Replace("Data", "Beat Saber_Data");
                if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, file)))
                    File.Delete(Path.Combine(App.BeatSaberInstallDirectory, file));
            }
            Pages.Options.Instance.YeetBSIPA.IsEnabled = false;
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            Mod mod = ((sender as System.Windows.Controls.Button).Tag as Mod);

            string title = string.Format((string)FindResource("Mods:UninstallBox:Title"), mod.name);
            string body1 = string.Format((string)FindResource("Mods:UninstallBox:Body1"), mod.name);
            string body2 = string.Format((string)FindResource("Mods:UninstallBox:Body2"), mod.name);
            var result = System.Windows.Forms.MessageBox.Show($"{body1}\n{body2}", title, MessageBoxButtons.YesNo);

            if (result == DialogResult.Yes)
            {
                UninstallModFromList(mod);
            }
        }

        private void UninstallModFromList(Mod mod)
        {
            UninstallMod(mod.ListItem.InstalledModInfo);
            mod.ListItem.IsInstalled = false;
            mod.ListItem.InstalledVersion = null;
            if (App.SelectInstalledMods)
            {
                mod.ListItem.IsSelected = false;
                UnresolveDependencies(mod);
                App.SavedMods.Remove(mod.name);
                Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
                Properties.Settings.Default.Save();
                RefreshModsList();
            }
            view.Refresh();
        }

        public void UninstallMod(Mod mod)
        {
            Mod.DownloadLink links = null;
            foreach (Mod.DownloadLink link in mod.downloads)
            {
                if (link.type.ToLower() == "universal" || link.type.ToLower() == App.BeatSaberInstallType.ToLower())
                {
                    links = link;
                    break;
                }
            }
            if (mod.name.ToLower() == "bsipa")
                UninstallBSIPA(links);
            foreach (Mod.FileHashes files in links.hashMd5)
            {
                if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, files.file)))
                    File.Delete(Path.Combine(App.BeatSaberInstallDirectory, files.file));
                if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, "IPA", "Pending", files.file)))
                    File.Delete(Path.Combine(App.BeatSaberInstallDirectory, "IPA", "Pending", files.file));
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}
