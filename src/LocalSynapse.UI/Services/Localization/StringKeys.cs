namespace LocalSynapse.UI.Services.Localization;

/// <summary>
/// Compile-time constants for localization keys. Prevents typo in XAML and code.
/// Naming: {Page}.{Section}.{Item} or {Domain}.{Item}.
/// </summary>
public static class StringKeys
{
    public static class Nav
    {
        public const string Search = "Nav.Search";
        public const string Data = "Nav.Data";
        public const string Mcp = "Nav.Mcp";
        public const string Security = "Nav.Security";
        public const string Settings = "Nav.Settings";
    }

    public static class Common
    {
        public const string All = "Common.All";
        public const string More = "Common.More";
        public const string Less = "Common.Less";
        public const string Copy = "Common.Copy";
        public const string Install = "Common.Install";
        public const string Installed = "Common.Installed";
    }

    public static class Filter
    {
        public const string Date30Days = "Filter.Date.30Days";
        public const string Date90Days = "Filter.Date.90Days";
        public const string DateThisYear = "Filter.Date.ThisYear";
    }

    public static class Folder
    {
        public const string FileCount = "Folder.FileCount";
        public const string FileCountWithDate = "Folder.FileCountWithDate";
    }

    public static class Search
    {
        public const string Placeholder = "Search.Placeholder";
        public const string Button = "Search.Button";
        public const string NoResults = "Search.NoResults";
        public const string NoResultsHint = "Search.NoResultsHint";
        public const string Initial = "Search.Initial";
        public const string InitialHint = "Search.InitialHint";
        public const string TryHint = "Search.TryHint";
        public const string QuotesHint = "Search.QuotesHint";
        public const string FilesIndexed = "Search.FilesIndexed";
        public const string Chunks = "Search.Chunks";
        public const string Results = "Search.Results";
        public const string FilteredEmpty = "Search.FilteredEmpty";
        public const string FilteredEmptyHint = "Search.FilteredEmptyHint";
        public const string SelectToPreview = "Search.SelectToPreview";

        public static class IndexedSummary
        {
            public const string Mac = "Search.IndexedSummary.Mac";
            public const string Desktop = "Search.IndexedSummary.Desktop";
        }

        public static class Section
        {
            public const string Filename = "Search.Section.Filename";
            public const string Opened = "Search.Section.Opened";
            public const string Content = "Search.Section.Content";
            public const string Folders = "Search.Section.Folders";
        }

        public static class Detail
        {
            public const string Info = "Search.Detail.Info";
            public const string ContentPreview = "Search.Detail.ContentPreview";
            public const string OpenFile = "Search.Detail.OpenFile";
            public const string OpenFolder = "Search.Detail.OpenFolder";
            public const string CopyPath = "Search.Detail.CopyPath";
            public const string Modified = "Search.Detail.Modified";
            public const string Size = "Search.Detail.Size";
            public const string Type = "Search.Detail.Type";
            public const string Score = "Search.Detail.Score";
            public const string Files = "Search.Detail.Files";
            public const string NotIndexed = "Search.Detail.NotIndexed";
            public const string PreviewError = "Search.Detail.PreviewError";
        }
    }

    public static class SmartNote
    {
        public const string NotLatest = "SmartNote.NotLatest";
        public const string Copy = "SmartNote.Copy";
        public const string LatestOfVersions = "SmartNote.LatestOfVersions";
        public const string NthOfVersions = "SmartNote.NthOfVersions";
        public const string OpenedToday = "SmartNote.OpenedToday";
        public const string OpenedDaysAgo = "SmartNote.OpenedDaysAgo";
        public const string OpenedLastWeek = "SmartNote.OpenedLastWeek";
        public const string OpenedLastMonth = "SmartNote.OpenedLastMonth";
        public const string FrequentOpened = "SmartNote.FrequentOpened";
        public const string FoundInTitle = "SmartNote.FoundInTitle";
        public const string FoundInFirstPage = "SmartNote.FoundInFirstPage";
        public const string FoundInPlaces = "SmartNote.FoundInPlaces";
        public const string ModifiedToday = "SmartNote.ModifiedToday";
        public const string ModifiedThisWeek = "SmartNote.ModifiedThisWeek";
        public const string ModifiedThisMonth = "SmartNote.ModifiedThisMonth";
        public const string NotModified2Years = "SmartNote.NotModified2Years";
        public const string RelatedFilesInFolder = "SmartNote.RelatedFilesInFolder";
        public const string PdfFinalVersion = "SmartNote.PdfFinalVersion";
        public const string HasPdfVersion = "SmartNote.HasPdfVersion";
        public const string HasPptVersion = "SmartNote.HasPptVersion";
    }

    public static class Data
    {
        public const string Header = "Data.Header";
        public const string Subtitle = "Data.Subtitle";
        public const string PipelineStatus = "Data.PipelineStatus";
        public const string Ready = "Data.Ready";
        public const string ScanTitle = "Data.Scan.Title";
        public const string ScanSubtitle = "Data.Scan.Subtitle";
        public const string ScanFilesFound = "Data.Scan.FilesFound";
        public const string ExtractTitle = "Data.Extract.Title";
        public const string ExtractSubtitle = "Data.Extract.Subtitle";
        public const string ExtractChunks = "Data.Extract.Chunks";
        public const string EmbedTitle = "Data.Embed.Title";
        public const string EmbedSubtitle = "Data.Embed.Subtitle";
        public const string EmbedStat = "Data.Embed.Stat";
        public const string ScanNow = "Data.ScanNow";
        public const string Pause = "Data.Pause";
        public const string Resume = "Data.Resume";
        public const string FilesSkipped = "Data.FilesSkipped";
        public const string FilesSkippedSuffix = "Data.FilesSkippedSuffix";
        public const string SkippedInfo = "Data.SkippedInfo";
        public const string AutoScanTitle = "Data.AutoScan.Title";
        public const string AutoScanInfo = "Data.AutoScan.Info";
        public const string ManualScanTitle = "Data.ManualScan.Title";
        public const string ManualScanInfo = "Data.ManualScan.Info";
        public const string AiSearchTitle = "Data.AiSearchTitle";
        public const string ModelTitle = "Data.Model.Title";
        public const string ModelSubtitle = "Data.Model.Subtitle";
    }

    public static class Security
    {
        public const string Header = "Security.Header";
        public const string Subtitle = "Security.Subtitle";
        public const string OfflineTitle = "Security.OfflineTitle";
        public const string OfflineDescription = "Security.OfflineDescription";
        public const string StorageTitle = "Security.StorageTitle";
        public const string StorageLocation = "Security.StorageLocation";
        public const string StorageSize = "Security.StorageSize";
        public const string OpenDataFolder = "Security.OpenDataFolder";
        public const string HowItWorksTitle = "Security.HowItWorksTitle";
        public const string HowItWorksBullet1 = "Security.HowItWorks.Bullet1";
        public const string HowItWorksBullet2 = "Security.HowItWorks.Bullet2";
        public const string HowItWorksBullet3 = "Security.HowItWorks.Bullet3";
        public const string HowItWorksBullet4 = "Security.HowItWorks.Bullet4";
        public const string HowItWorksBullet5 = "Security.HowItWorks.Bullet5";
        public const string PingTitle = "Security.PingTitle";
        public const string PingDescription = "Security.PingDescription";

        public static class Sends
        {
            public const string Title = "Security.Sends.Title";
            public const string Subtitle = "Security.Sends.Subtitle";
            public const string Receives = "Security.Sends.Receives";
            public const string SendsLabel = "Security.Sends.Sends";
            public const string Frequency = "Security.Sends.Frequency";
            public const string Toggle = "Security.Sends.Toggle";
            public const string ExpandTitle = "Security.Sends.ExpandTitle";
            public const string ExpandBody = "Security.Sends.ExpandBody";
            public const string ConfirmTitle = "Security.Sends.ConfirmTitle";
            public const string ConfirmBody = "Security.Sends.ConfirmBody";
            public const string ConfirmAction = "Security.Sends.ConfirmAction";
            public const string ConfirmCancel = "Security.Sends.ConfirmCancel";
        }
    }

    public static class Settings
    {
        public const string Header = "Settings.Header";
        public const string Subtitle = "Settings.Subtitle";
        public const string LanguageTitle = "Settings.Language.Title";
        public const string LanguageCurrent = "Settings.Language.Current";
        public const string AboutTitle = "Settings.About.Title";
        public const string AboutVersion = "Settings.About.Version";
        public const string AboutData = "Settings.About.Data";
        public const string AboutLicense = "Settings.About.License";
        public const string LinksTitle = "Settings.Links.Title";
        public const string LinksWebsite = "Settings.Links.Website";
        public const string LinksGitHub = "Settings.Links.GitHub";
        public const string LanguageSearchHint = "Settings.Language.SearchHint";
    }

    public static class Mcp
    {
        public const string Header = "Mcp.Header";
        public const string Subtitle = "Mcp.Subtitle";
        public const string ServerInfoTitle = "Mcp.ServerInfoTitle";
        public const string Executable = "Mcp.Executable";
        public const string Transport = "Mcp.Transport";
        public const string TransportValue = "Mcp.TransportValue";
        public const string Command = "Mcp.Command";
        public const string ClientsTitle = "Mcp.ClientsTitle";
        public const string Connect = "Mcp.Connect";
        public const string Disconnect = "Mcp.Disconnect";
        public const string OpenConfigFolder = "Mcp.OpenConfigFolder";
        public const string RegisteredNotice = "Mcp.RegisteredNotice";
        public const string RunCommandHint = "Mcp.RunCommandHint";
        public const string ToRemoveHint = "Mcp.ToRemoveHint";
        public const string AvailableToolsTitle = "Mcp.AvailableToolsTitle";
        public const string QuickStartTitle = "Mcp.QuickStartTitle";
        public const string QuickStart1Title = "Mcp.QuickStart1.Title";
        public const string QuickStart1Desc = "Mcp.QuickStart1.Desc";
        public const string QuickStart2Title = "Mcp.QuickStart2.Title";
        public const string QuickStart2Desc = "Mcp.QuickStart2.Desc";
        public const string QuickStart3Title = "Mcp.QuickStart3.Title";
        public const string QuickStart3Desc = "Mcp.QuickStart3.Desc";
        public const string NoteLabel = "Mcp.NoteLabel";
        public const string NoteText = "Mcp.NoteText";
    }

    public static class UpdateCheck
    {
        public const string Available = "UpdateCheck.Available";
        public const string Download = "UpdateCheck.Download";
        public const string WhatsNew = "UpdateCheck.WhatsNew";
        public const string Dismiss = "UpdateCheck.Dismiss";
        public const string Toggle = "UpdateCheck.Toggle";
        // FirstRunNotice/Body/Ok/Disable removed (WO-SEC0)
        public const string WhatsNewTitle = "UpdateCheck.WhatsNewTitle";
        public const string UpToDate = "UpdateCheck.UpToDate";
        public const string UpdateAvailable = "UpdateCheck.UpdateAvailable";
    }

    public static class Welcome
    {
        public const string Title = "Welcome.Title";
        public const string Subtitle = "Welcome.Subtitle";
        public const string ScanAll = "Welcome.ScanAll";
        public const string ScanAllDesc = "Welcome.ScanAll.Desc";
        public const string MyDocs = "Welcome.MyDocs";
        public const string MyDocsDesc = "Welcome.MyDocs.Desc";
        public const string Custom = "Welcome.Custom";
        public const string CustomDesc = "Welcome.Custom.Desc";
        public const string AddFolder = "Welcome.AddFolder";
        public const string Start = "Welcome.Start";
    }
}
