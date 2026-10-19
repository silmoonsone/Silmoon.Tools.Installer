using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Silmoon.Tools.WindowsInstallerMaker.Core;
using Silmoon.Tools.WindowsInstallerMaker.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Silmoon.Tools.WindowsInstallerMaker.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private IEditorInteraction Interaction { get; }
    private ProjectStore Store { get; } = new();
    private bool LoadingEditor { get; set; }
    private CancellationTokenSource? BuildCancellation { get; set; }
    private static JsonSerializerOptions DependencyJsonOptions { get; } = new() { WriteIndented = true, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) } };
    public ObservableCollection<string> DependencySummary { get; } = [];
    public ObservableCollection<string> Artifacts { get; } = [];
    public IReadOnlyList<EnumChoice<TargetArchitecture>> Architectures { get; } = EnumChoices.For<TargetArchitecture>();
    public IReadOnlyList<EnumChoice<DependencyKind>> DependencyKinds { get; } = EnumChoices.For<DependencyKind>();
    public IReadOnlyList<EnumChoice<DependencyDelivery>> DependencyDeliveries { get; } = EnumChoices.For<DependencyDelivery>();
    public IReadOnlyList<EnumChoice<InstallScope>> InstallScopes { get; } = EnumChoices.For<InstallScope>();
    public IReadOnlyList<EnumChoice<DistributionMode>> DistributionModes { get; } = EnumChoices.For<DistributionMode>();
    public IReadOnlyList<EnumChoice<SigningMode>> SigningModes { get; } = EnumChoices.For<SigningMode>();

    public IReadOnlyList<string> ArchitectureLabels { get; } = EnumChoices.For<TargetArchitecture>().Select(x => x.Name).ToArray();
    public int ArchitectureIndex { get => Architectures.ToList().FindIndex(x => x.Value == Architecture); set { if (value >= 0 && value < Architectures.Count) Architecture = Architectures[value].Value; } }
    partial void OnArchitectureChanged(TargetArchitecture value) => OnPropertyChanged(nameof(ArchitectureIndex));
    public IReadOnlyList<string> InstallScopeLabels { get; } = EnumChoices.For<InstallScope>().Select(x => x.Name).ToArray();
    public int InstallScopeIndex { get => InstallScopes.ToList().FindIndex(x => x.Value == InstallScope); set { if (value >= 0 && value < InstallScopes.Count) InstallScope = InstallScopes[value].Value; } }
    partial void OnInstallScopeChanged(InstallScope value) => OnPropertyChanged(nameof(InstallScopeIndex));
    public IReadOnlyList<string> NewDependencyKindLabels { get; } = EnumChoices.For<DependencyKind>().Select(x => x.Name).ToArray();
    public int NewDependencyKindIndex { get => DependencyKinds.ToList().FindIndex(x => x.Value == NewDependencyKind); set { if (value >= 0 && value < DependencyKinds.Count) NewDependencyKind = DependencyKinds[value].Value; } }
    partial void OnNewDependencyKindChanged(DependencyKind value) => OnPropertyChanged(nameof(NewDependencyKindIndex));
    public IReadOnlyList<string> NewDependencyDeliveryLabels { get; } = EnumChoices.For<DependencyDelivery>().Select(x => x.Name).ToArray();
    public int NewDependencyDeliveryIndex { get => DependencyDeliveries.ToList().FindIndex(x => x.Value == NewDependencyDelivery); set { if (value >= 0 && value < DependencyDeliveries.Count) NewDependencyDelivery = DependencyDeliveries[value].Value; } }
    partial void OnNewDependencyDeliveryChanged(DependencyDelivery value) => OnPropertyChanged(nameof(NewDependencyDeliveryIndex));
    public IReadOnlyList<string> DistributionModeLabels { get; } = EnumChoices.For<DistributionMode>().Select(x => x.Name).ToArray();
    public int DistributionModeIndex { get => DistributionModes.ToList().FindIndex(x => x.Value == DistributionMode); set { if (value >= 0 && value < DistributionModes.Count) DistributionMode = DistributionModes[value].Value; } }
    partial void OnDistributionModeChanged(DistributionMode value) => OnPropertyChanged(nameof(DistributionModeIndex));
    public IReadOnlyList<string> SigningModeLabels { get; } = EnumChoices.For<SigningMode>().Select(x => x.Name).ToArray();
    public int SigningModeIndex { get => SigningModes.ToList().FindIndex(x => x.Value == SigningMode); set { if (value >= 0 && value < SigningModes.Count) SigningMode = SigningModes[value].Value; } }
    partial void OnSigningModeChanged(SigningMode value) => OnPropertyChanged(nameof(SigningModeIndex));

    [ObservableProperty] public partial string OpenPath { get; set; } = "";
    [ObservableProperty] public partial string ProjectFilePath { get; set; } = "";
    [ObservableProperty] public partial string ProductId { get; set; } = "";
    [ObservableProperty] public partial string DisplayName { get; set; } = "";
    [ObservableProperty] public partial string Publisher { get; set; } = "";
    [ObservableProperty] public partial string Version { get; set; } = "1.0.0";
    [ObservableProperty] public partial string PublishDirectory { get; set; } = "";
    [ObservableProperty] public partial string MainExecutable { get; set; } = "";
    [ObservableProperty] public partial string OutputDirectory { get; set; } = "";
    [ObservableProperty] public partial TargetArchitecture Architecture { get; set; } = TargetArchitecture.X64;
    [ObservableProperty] public partial string Channel { get; set; } = "win-x64";
    [ObservableProperty] public partial string IconPath { get; set; } = "";
    [ObservableProperty] public partial bool DesktopShortcut { get; set; } = true;
    [ObservableProperty] public partial bool StartMenuShortcut { get; set; } = true;
    [ObservableProperty] public partial InstallScope InstallScope { get; set; } = InstallScope.CurrentUser;
    [ObservableProperty] public partial DistributionMode DistributionMode { get; set; } = DistributionMode.FullApplication;
    [ObservableProperty] public partial SigningMode SigningMode { get; set; } = SigningMode.None;
    [ObservableProperty] public partial string UpdateUrl { get; set; } = "";
    [ObservableProperty] public partial string ReleaseNotesPath { get; set; } = "";
    [ObservableProperty] public partial string ExpectedVelopackVersion { get; set; } = "1.2.161";
    [ObservableProperty] public partial string Notes { get; set; } = "";
    [ObservableProperty] public partial string DependenciesJson { get; set; } = "[]";
    [ObservableProperty] public partial DependencyKind NewDependencyKind { get; set; } = DependencyKind.WebView2;
    [ObservableProperty] public partial DependencyDelivery NewDependencyDelivery { get; set; } = DependencyDelivery.OfflineSidecar;
    [ObservableProperty] public partial bool PreserveUserData { get; set; } = true;
    [ObservableProperty] public partial bool GenerateDelta { get; set; }
    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsError { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = TextResources.Get("Message98");
    [ObservableProperty] public partial string AnalysisText { get; set; } = TextResources.Get("Message99");
    [ObservableProperty] public partial string BuildLog { get; set; } = "";
    [ObservableProperty] public partial string BuildStage { get; set; } = TextResources.Get("Message100");
    [ObservableProperty] public partial double BuildPercent { get; set; }
    [ObservableProperty] public partial bool ProgressIndeterminate { get; set; }
    [ObservableProperty] public partial string SetupPath { get; set; } = "";
    [ObservableProperty] public partial string LastOutputDirectory { get; set; } = "";
    [ObservableProperty] public partial string ExitCodeText { get; set; } = "—";
    public bool CanEdit => !IsBusy;
    public string DocumentState => IsDirty ? TextResources.Get("Message101") : TextResources.Get("Message102");

    public MainPageViewModel(IEditorInteraction interaction)
    {
        Interaction = interaction;
        PropertyChanged += OnEditorPropertyChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (LoadingEditor) return;
        if (args.PropertyName is nameof(ProductId) or nameof(DisplayName) or nameof(Publisher) or nameof(Version) or nameof(PublishDirectory) or nameof(MainExecutable) or nameof(OutputDirectory) or nameof(Architecture) or nameof(Channel) or nameof(IconPath) or nameof(DesktopShortcut) or nameof(StartMenuShortcut) or nameof(InstallScope) or nameof(DistributionMode) or nameof(SigningMode) or nameof(UpdateUrl) or nameof(ReleaseNotesPath) or nameof(ExpectedVelopackVersion) or nameof(Notes) or nameof(DependenciesJson) or nameof(PreserveUserData) or nameof(GenerateDelta)) IsDirty = true;
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(DocumentState));
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        NewCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        OpenPathCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        AnalyzeCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
        BuildCommand.NotifyCanExecuteChanged();
        BrowseCommand.NotifyCanExecuteChanged();
        AddDependencyCommand.NotifyCanExecuteChanged();
        ApplyDependenciesCommand.NotifyCanExecuteChanged();
        CancelBuildCommand.NotifyCanExecuteChanged();
    }

    private List<DependencyDefinition> ReadDependencies() => JsonSerializer.Deserialize<List<DependencyDefinition>>(DependenciesJson, DependencyJsonOptions) ?? throw new InvalidDataException(TextResources.Get("Message103"));

    private InstallerProject Snapshot() => new() { ProductId = ProductId.Trim(), DisplayName = DisplayName.Trim(), Publisher = Publisher.Trim(), Version = Version.Trim(), PublishDirectory = PublishDirectory.Trim(), MainExecutable = MainExecutable.Trim(), OutputDirectory = OutputDirectory.Trim(), Architecture = Architecture, Channel = Channel.Trim(), IconPath = IconPath.Trim(), DesktopShortcut = DesktopShortcut, StartMenuShortcut = StartMenuShortcut, InstallScope = InstallScope, DistributionMode = DistributionMode, SigningMode = SigningMode, UpdateUrl = UpdateUrl.Trim(), ReleaseNotesPath = ReleaseNotesPath.Trim(), ExpectedVelopackVersion = ExpectedVelopackVersion.Trim(), Notes = Notes, Dependencies = ReadDependencies(), PreserveUserData = PreserveUserData, GenerateDelta = GenerateDelta };

    private void ApplyProject(InstallerProject project, string path)
    {
        LoadingEditor = true;
        try
        {
            ProductId = project.ProductId; DisplayName = project.DisplayName; Publisher = project.Publisher; Version = project.Version;
            PublishDirectory = project.PublishDirectory; MainExecutable = project.MainExecutable; OutputDirectory = project.OutputDirectory; Architecture = project.Architecture;
            Channel = project.Channel; IconPath = project.IconPath; DesktopShortcut = project.DesktopShortcut; StartMenuShortcut = project.StartMenuShortcut; InstallScope = project.InstallScope;
            DistributionMode = project.DistributionMode; SigningMode = project.SigningMode; UpdateUrl = project.UpdateUrl; ReleaseNotesPath = project.ReleaseNotesPath;
            ExpectedVelopackVersion = project.ExpectedVelopackVersion; Notes = project.Notes; PreserveUserData = project.PreserveUserData; GenerateDelta = project.GenerateDelta;
            DependenciesJson = JsonSerializer.Serialize(project.Dependencies, DependencyJsonOptions);
            ProjectFilePath = path; OpenPath = path; IsDirty = false;
            RefreshDependencySummary(project.Dependencies);
        }
        finally { LoadingEditor = false; }
    }

    private void RefreshDependencySummary(IEnumerable<DependencyDefinition> dependencies)
    {
        DependencySummary.Clear();
        foreach (var item in dependencies) DependencySummary.Add($"{item.Name} · {item.Kind} · {item.Delivery} · {item.MinimumVersion}");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task NewAsync()
    {
        try
        {
            if (!await ResolveUnsavedAsync()) return;
            ApplyProject(new(), "");
            SetupPath = ""; LastOutputDirectory = ""; Artifacts.Clear(); AnalysisText = TextResources.Get("Message104");
            SetStatus(TextResources.Get("Message105"));
        }
        catch (Exception ex) { ReportError(ex); }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task OpenAsync()
    {
        try
        {
            var path = await Interaction.OpenFileAsync(".json");
            if (path is not null) await LoadPathAsync(path);
        }
        catch (Exception ex) { ReportError(ex); }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task OpenPathAsync()
    {
        try { await LoadPathAsync(OpenPath.Trim()); }
        catch (Exception ex) { ReportError(ex); }
    }

    private async Task LoadPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException(TextResources.Get("Message106"));
        if (!await ResolveUnsavedAsync()) return;
        IsBusy = true;
        try
        {
            var project = await Store.LoadAsync(path);
            ApplyProject(project, Path.GetFullPath(path));
            SetupPath = ""; LastOutputDirectory = ""; Artifacts.Clear(); AnalysisText = TextResources.Get("Message107");
            SetStatus(TextResources.Format("ProjectLoaded", Path.GetFileName(path)));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        try { await SaveProjectAsync(false); }
        catch (Exception ex) { ReportError(ex); }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsAsync()
    {
        try { await SaveProjectAsync(true); }
        catch (Exception ex) { ReportError(ex); }
    }

    private async Task<bool> SaveProjectAsync(bool saveAs)
    {
        var path = ProjectFilePath;
        if (saveAs || string.IsNullOrEmpty(path)) path = await Interaction.SaveFileAsync(string.IsNullOrEmpty(ProductId) ? "installer-project" : ProductId);
        if (string.IsNullOrEmpty(path)) return false;
        IsBusy = true;
        try
        {
            var saved = await Store.SaveAsync(Snapshot(), path, string.IsNullOrEmpty(ProjectFilePath) ? null : ProjectFilePath);
            ApplyProject(saved, Path.GetFullPath(path));
            SetStatus(TextResources.Format("ProjectSaved", Path.GetFileName(path)));
            return true;
        }
        finally { IsBusy = false; }
    }

    private async Task<bool> ResolveUnsavedAsync()
    {
        if (!IsDirty) return true;
        return (await Interaction.ConfirmUnsavedAsync()) switch { SaveDecision.Save => await SaveProjectAsync(false), SaveDecision.Discard => true, _ => false };
    }

    public async Task<bool> CanCloseAsync()
    {
        try
        {
            if (IsBusy)
            {
                if (BuildCancellation is not null && await Interaction.ConfirmAsync(TextResources.Get("Message108"), TextResources.Get("Message109"), TextResources.Get("Message110"))) BuildCancellation.Cancel();
                return false;
            }
            return await ResolveUnsavedAsync();
        }
        catch (Exception ex) { ReportError(ex); return false; }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task BrowseAsync(string field)
    {
        try
        {
            var path = field is "publish" or "output" ? await Interaction.PickFolderAsync() : await Interaction.OpenFileAsync(field == "icon" ? ".ico" : field == "notes" ? ".md" : field == "dependency" ? ".exe" : ".exe");
            if (path is null) return;
            switch (field)
            {
                case "publish": PublishDirectory = path; break;
                case "output": OutputDirectory = path; break;
                case "main": MainExecutable = Path.GetRelativePath(ProjectStore.ResolvePath(PublishDirectory, ProjectFilePath), path); break;
                case "icon": IconPath = path; break;
                case "notes": ReleaseNotesPath = path; break;
                case "dependency":
                    var dependencies = ReadDependencies();
                    if (dependencies.Count == 0) throw new InvalidDataException(TextResources.Get("Message111"));
                    dependencies[^1].OfflineInstallerPath = path;
                    DependenciesJson = JsonSerializer.Serialize(dependencies, DependencyJsonOptions);
                    RefreshDependencySummary(dependencies);
                    break;
            }
        }
        catch (Exception ex) { ReportError(ex); }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddDependency()
    {
        try
        {
            var dependencies = ReadDependencies();
            var name = DependencyKinds.First(x => x.Value.Equals(NewDependencyKind)).Name;
            var dependency = DependencyCatalog.Presets.FirstOrDefault(x => x.Kind == NewDependencyKind) ?? new DependencyDefinition { Kind = NewDependencyKind, Name = name };
            dependency.Id = $"{NewDependencyKind.ToString().ToLowerInvariant()}-{dependencies.Count + 1}";
            dependency.Delivery = NewDependencyDelivery; dependency.Architecture = Architecture; dependency.FrameworkId = "";
            dependency.AdvancedDevelopmentTool = NewDependencyKind is DependencyKind.DotNetSdk or DependencyKind.WindowsSdk;
            if (NewDependencyDelivery == DependencyDelivery.Online && DependencyCatalog.TryGetFrameworkId(dependency, out var frameworkId)) dependency.FrameworkId = frameworkId;
            dependencies.Add(dependency);
            DependenciesJson = JsonSerializer.Serialize(dependencies, DependencyJsonOptions);
            RefreshDependencySummary(dependencies);
            SetStatus(TextResources.Get("Message112"));
        }
        catch (Exception ex) { ReportError(ex); }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ApplyDependencies()
    {
        try { RefreshDependencySummary(ReadDependencies()); SetStatus(TextResources.Get("Message113")); }
        catch (Exception ex) { ReportError(ex); }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task AnalyzeAsync()
    {
        IsBusy = true;
        try
        {
            var report = await new PublishAnalyzer().AnalyzeAsync(ProjectStore.ResolvePath(PublishDirectory, ProjectFilePath), MainExecutable);
            AnalysisText = TextResources.Format("AnalysisSummary", report.Architecture, report.TargetFramework, report.RuntimeIdentifier, report.IsDotNetSelfContained, report.IsWindowsAppSdkSelfContained, report.HasVelopackReference, report.VelopackVersion, report.MainProductName, report.MainFileVersion) + string.Join("\n", report.Issues.Select(x => $"[{x.Severity}] {x.Message}").Concat(report.Suggestions));
            SetStatus(TextResources.Get("Message114"), report.Issues.Any(x => x.Severity == IssueSeverity.Error));
        }
        catch (Exception ex) { ReportError(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Validate()
    {
        try
        {
            var result = new ProjectValidator().Validate(Snapshot(), ProjectFilePath);
            AnalysisText = result.Issues.Count == 0 ? TextResources.Get("Message115") : string.Join("\n", result.Issues.Select(x => $"[{x.Severity}] {x.Code}: {x.Message}"));
            SetStatus(result.IsValid ? TextResources.Get("Message116") : TextResources.Get("Message117"), !result.IsValid);
        }
        catch (Exception ex) { ReportError(ex); }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task BuildAsync()
    {
        SetupPath = ""; LastOutputDirectory = ""; Artifacts.Clear(); BuildPercent = 0; ExitCodeText = "—"; BuildLog = ""; BuildStage = TextResources.Get("PreparingBuild");
        try
        {
            if ((IsDirty || string.IsNullOrEmpty(ProjectFilePath)) && !await SaveProjectAsync(false)) return;
            var snapshot = Snapshot();
            var validation = new ProjectValidator().Validate(snapshot, ProjectFilePath);
            if (!validation.IsValid) throw new InvalidDataException(string.Join("\n", validation.Issues.Where(x => x.Severity == IssueSeverity.Error).Select(x => x.Message)));
            BuildCancellation = new();
            IsBusy = true; SetupPath = ""; LastOutputDirectory = ""; Artifacts.Clear(); BuildLog = ""; BuildPercent = 0; ExitCodeText = "—";
            SetStatus(TextResources.Get("Message118"));
            var progress = new Progress<BuildProgress>(item => { BuildStage = item.Stage; ProgressIndeterminate = !item.Percent.HasValue; BuildPercent = item.Percent ?? BuildPercent; AppendLog($"[{item.Stage}] {item.Message}"); });
            var result = await new VelopackEngine().BuildAsync(snapshot, ProjectFilePath, progress, BuildCancellation.Token);
            ExitCodeText = result.ExitCode?.ToString() ?? "—";
            BuildStage = result.Canceled ? TextResources.Get("Message119") : result.Success ? TextResources.Get("Message120") : TextResources.Get("Message121");
            SetStatus(result.Message, !result.Success && !result.Canceled);
            if (result.Success)
            {
                SetupPath = result.SetupPath; LastOutputDirectory = result.OutputDirectory; BuildPercent = 100;
                foreach (var artifact in result.Artifacts) Artifacts.Add($"{artifact.Kind} · {artifact.Size / 1048576.0:N2} MiB\n{artifact.Path}\nSHA256: {artifact.Sha256}");
            }
            AppendLog(TextResources.Format("BuildFinished", BuildStage, ExitCodeText, result.Message, result.LogPath));
        }
        catch (Exception ex) { BuildStage = TextResources.Get("Message122"); ReportError(ex); }
        finally { ProgressIndeterminate = false; BuildCancellation?.Dispose(); BuildCancellation = null; IsBusy = false; }
    }

    private bool CanCancelBuild() => IsBusy && BuildCancellation is not null;
    [RelayCommand(CanExecute = nameof(CanCancelBuild))]
    private void CancelBuild() { BuildCancellation?.Cancel(); BuildStage = TextResources.Get("Message123"); }

    [RelayCommand]
    private async Task OpenOutputAsync()
    {
        try { await Interaction.OpenDirectoryAsync(string.IsNullOrEmpty(LastOutputDirectory) ? ProjectStore.ResolvePath(OutputDirectory, ProjectFilePath) : LastOutputDirectory); }
        catch (Exception ex) { ReportError(ex); }
    }

    private void AppendLog(string text)
    {
        var next = BuildLog + text + Environment.NewLine;
        BuildLog = next.Length > 150000 ? next[^150000..] : next;
    }

    private void SetStatus(string message, bool error = false) { IsError = error; Status = message; }
    private void ReportError(Exception exception)
    {
        SetStatus(exception.Message.Split('\n')[0], true);
        AnalysisText = exception.Message;
        AppendLog(TextResources.Format("ErrorDetail", exception.Message));
    }
}
