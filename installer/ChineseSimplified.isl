; *** Inno Setup version 6.1.0+ Chinese Simplified messages ***
; Maintained for LocalSynapse project
; Based on Inno Setup standard ISL format

[LangOptions]
LanguageName=<7B80><4F53><4E2D><6587>
LanguageID=$0804
LanguageCodePage=936

[Messages]
SetupAppTitle=安装
SetupWindowTitle=安装 - %1
UninstallAppTitle=卸载
UninstallAppFullTitle=%1 卸载

; *** Misc
InformationTitle=信息
ConfirmTitle=确认
ErrorTitle=错误

; *** SetupLdr messages
SetupLdrStartupMessage=现在将安装 %1。您想要继续吗？
LdrCannotCreateTemp=无法创建临时文件。安装中止
LdrCannotExecTemp=无法执行临时目录中的文件。安装中止
HelpTextNote=

; *** Startup error messages
LastErrorMessage=%1.%n%n错误 %2: %3
SetupFileMissing=安装目录中缺少文件 %1。请纠正此问题或获取新的程序副本。
SetupFileCorrupt=安装文件已损坏。请获取新的程序副本。
SetupFileCorruptOrWrongVer=安装文件已损坏或与此版本的安装程序不兼容。请纠正此问题或获取新的程序副本。
InvalidParameter=命令行中传递了无效的参数：%n%n%1
SetupAlreadyRunning=安装程序已在运行。
WindowsVersionNotSupported=此程序不支持您计算机运行的 Windows 版本。
WindowsServicePackRequired=此程序需要 %1 Service Pack %2 或更高版本。
NotOnThisPlatform=此程序不能在 %1 上运行。
OnlyOnThisPlatform=此程序必须在 %1 上运行。
OnlyOnTheseArchitectures=此程序只能安装在为以下处理器架构设计的 Windows 版本上：%n%n%1
WinVersionTooHighError=此程序不能安装在 %1 版本 %2 或更高版本上。
WinVersionTooLowError=此程序需要 %1 版本 %2 或更高版本。
MissingWOW64APIs=您运行的 Windows 版本不包含执行 64 位安装所需的功能。
X64LikeInstructionSetRequired=此程序需要支持 %1 指令集扩展的处理器。

; *** Startup questions
PrivilegesRequiredOverrideTitle=选择安装模式
PrivilegesRequiredOverrideInstruction=选择安装模式
PrivilegesRequiredOverrideText1=%1 可以为所有用户安装（需要管理员权限），也可以仅为您安装。
PrivilegesRequiredOverrideText2=%1 可以仅为您安装，也可以为所有用户安装（需要管理员权限）。
PrivilegesRequiredOverrideAllUsers=为所有用户安装(&A)
PrivilegesRequiredOverrideAllUsersRecommended=为所有用户安装（推荐）(&A)
PrivilegesRequiredOverrideCurrentUser=仅为我安装(&M)
PrivilegesRequiredOverrideCurrentUserRecommended=仅为我安装（推荐）(&M)

; *** Misc
ButtonBack=< 上一步(&B)
ButtonNext=下一步(&N) >
ButtonInstall=安装(&I)
ButtonOK=确定
ButtonCancel=取消
ButtonYes=是(&Y)
ButtonYesToAll=全部是(&A)
ButtonNo=否(&N)
ButtonNoToAll=全部否(&O)
ButtonFinish=完成(&F)
ButtonBrowse=浏览(&B)...
ButtonWizardBrowse=浏览(&R)...
ButtonNewFolder=新建文件夹(&M)

SelectLanguageTitle=选择安装语言
SelectLanguageLabel=选择安装过程中要使用的语言：

; *** Common wizard text
ClickNext=点击"下一步"继续，或点击"取消"退出安装。
BeveledLabel=
BrowseDialogTitle=浏览文件夹
BrowseDialogLabel=在下面的列表中选择一个文件夹，然后点击"确定"。
NewFolderName=新建文件夹

; *** "Welcome" wizard page
WelcomeLabel1=欢迎使用 [name] 安装向导
WelcomeLabel2=这将在您的计算机上安装 [name/ver]。%n%n建议您在继续之前关闭所有其他应用程序。

; *** "License" wizard page
WizardLicense=许可协议
LicenseLabel=请在继续安装之前阅读以下重要信息。
LicenseLabel3=请阅读以下许可协议。您必须接受协议中的条款才能继续安装。
LicenseAccepted=我接受协议(&A)
LicenseNotAccepted=我不接受协议(&D)

; *** "Information" wizard page
WizardInfoBefore=信息
InfoBeforeLabel=请在继续安装之前阅读以下重要信息。
InfoBeforeClickLabel=准备好继续安装后，请点击"下一步"。
WizardInfoAfter=信息
InfoAfterLabel=请在继续安装之前阅读以下重要信息。
InfoAfterClickLabel=准备好继续安装后，请点击"下一步"。

; *** "Select Destination" wizard page
WizardUserInfo=用户信息
UserInfoDesc=请输入您的信息。
UserInfoName=用户名(&U)：
UserInfoOrg=组织(&O)：
UserInfoSerial=序列号(&S)：
UserInfoNameRequired=您必须输入用户名。

; *** "Select Destination Location" wizard page
WizardSelectDir=选择安装位置
SelectDirDesc=将 [name] 安装到哪里？
SelectDirLabel3=安装程序将把 [name] 安装到以下文件夹中。
SelectDirBrowseLabel=要继续，请点击"下一步"。如果要选择其他文件夹，请点击"浏览"。
DiskSpaceGBLabel=需要至少 [gb] GB 的可用磁盘空间。
DiskSpaceMBLabel=需要至少 [mb] MB 的可用磁盘空间。
CannotInstallToNetworkDrive=安装程序无法安装到网络驱动器上。
CannotInstallToUNCPath=安装程序无法安装到 UNC 路径中。
InvalidPath=您必须输入完整的路径（含驱动器号）；例如：%n%nC:\APP%n%n或以下格式的 UNC 路径：%n%n\\server\share
InvalidDrive=您选择的驱动器或 UNC 共享不存在或无法访问。请选择其他位置。
DiskSpaceWarningTitle=磁盘空间不足
DiskSpaceWarning=安装需要至少 %1 KB 的可用空间，但所选驱动器只有 %2 KB 可用。%n%n您是否仍要继续？
DirNameTooLong=文件夹名称或路径太长。
InvalidDirName=文件夹名称无效。
BadDirName32=文件夹名称不能包含以下字符：%n%n%1
DirExistsTitle=文件夹已存在
DirExists=文件夹 %n%n%1%n%n 已存在。您仍然要安装到该文件夹吗？
DirDoesntExistTitle=文件夹不存在
DirDoesntExist=文件夹 %n%n%1%n%n 不存在。您想要创建该文件夹吗？

; *** "Select Components" wizard page
WizardSelectComponents=选择组件
SelectComponentsDesc=应安装哪些组件？
SelectComponentsLabel2=选择要安装的组件；取消选择不想安装的组件。准备好后点击"下一步"继续。
FullInstallation=完全安装
CompactInstallation=紧凑安装
CustomInstallation=自定义安装
NoUninstallWarningTitle=组件已存在
NoUninstallWarning=安装程序检测到以下组件已安装在您的计算机上：%n%n%1%n%n取消选择这些组件将不会卸载它们。%n%n您是否仍要继续？
ComponentSize1=%1 KB
ComponentSize2=%1 MB
ComponentsDiskSpaceGBLabel=当前选择需要至少 [gb] GB 的磁盘空间。
ComponentsDiskSpaceMBLabel=当前选择需要至少 [mb] MB 的磁盘空间。

; *** "Select Additional Tasks" wizard page
WizardSelectTasks=选择附加任务
SelectTasksDesc=应执行哪些附加任务？
SelectTasksLabel2=选择在安装 [name] 过程中要执行的附加任务，然后点击"下一步"。

; *** "Select Start Menu Folder" wizard page
WizardSelectProgramGroup=选择开始菜单文件夹
SelectStartMenuFolderDesc=安装程序应在哪里放置快捷方式？
SelectStartMenuFolderLabel3=安装程序将在以下开始菜单文件夹中创建快捷方式。
SelectStartMenuFolderBrowseLabel=要继续，请点击"下一步"。如果要选择其他文件夹，请点击"浏览"。
MustEnterGroupName=您必须输入文件夹名称。
GroupNameTooLong=文件夹名称或路径太长。
InvalidGroupName=文件夹名称无效。
BadGroupName=文件夹名称不能包含以下字符：%n%n%1
NoProgramGroupCheck2=不创建开始菜单文件夹(&D)

; *** "Ready to Install" wizard page
WizardReady=准备安装
ReadyLabel1=安装程序现在准备在您的计算机上安装 [name]。
ReadyLabel2a=点击"安装"继续安装，或者点击"上一步"更改设置。
ReadyLabel2b=点击"安装"继续安装。
ReadyMemoUserInfo=用户信息：
ReadyMemoDir=安装位置：
ReadyMemoType=安装类型：
ReadyMemoComponents=选定组件：
ReadyMemoGroup=开始菜单文件夹：
ReadyMemoTasks=附加任务：

; *** "Preparing to Install" wizard page
WizardPreparing=正在准备安装
PreparingDesc=安装程序正在准备在您的计算机上安装 [name]。
PreviousInstallNotCompleted=上一个安装/卸载未完成。您需要重新启动计算机来完成该操作。%n%n重新启动计算机后，请再次运行安装程序来完成 [name] 的安装。
CannotContinue=安装程序无法继续。请点击"取消"退出。
ApplicationsFound=以下应用程序正在使用需要安装程序更新的文件。建议您允许安装程序自动关闭这些应用程序。
ApplicationsFound2=以下应用程序正在使用需要安装程序更新的文件。建议您允许安装程序自动关闭这些应用程序。安装完成后，安装程序将尝试重新启动这些应用程序。
CloseApplications=自动关闭应用程序(&A)
DontCloseApplications=不关闭应用程序(&D)
ErrorCloseApplications=安装程序无法自动关闭所有应用程序。建议您在继续之前关闭所有使用需要安装程序更新的文件的应用程序。
PrepareToInstallNeedsRestart=安装程序需要重新启动计算机。重新启动后，请再次运行安装程序来完成 [name] 的安装。%n%n现在重新启动吗？

; *** "Installing" wizard page
WizardInstalling=正在安装
InstallingLabel=请稍候，安装程序正在将 [name] 安装到您的计算机上。

; *** "Setup Completed" wizard page
FinishedHeadingLabel=[name] 安装完成
FinishedLabelNoIcons=已在您的计算机上完成 [name] 的安装。
FinishedLabel=已在您的计算机上完成 [name] 的安装。可以通过已创建的快捷方式启动应用程序。
ClickFinish=点击"完成"退出安装向导。
FinishedRestartLabel=要完成 [name] 的安装，安装程序必须重新启动计算机。您想现在重新启动吗？
FinishedRestartMessage=要完成 [name] 的安装，安装程序必须重新启动计算机。%n%n您想现在重新启动吗？
ShowReadmeCheck=是，我想查看自述文件
YesRadio=是，立即重新启动计算机(&Y)
NoRadio=否，稍后重新启动计算机(&N)
RunEntryExec=运行 %1
RunEntryShellExec=查看 %1

; *** "Setup Needs the Next Disk" stuff
ChangeDiskTitle=安装程序需要下一张磁盘
SelectDiskLabel2=请插入磁盘 %1 并点击"确定"。%n%n如果该磁盘中的文件可以在以下文件夹以外的其他文件夹中找到，请输入正确的路径或点击"浏览"。
PathLabel=路径(&P)：
FileNotInDir2=在"%2"中找不到文件"%1"。请插入正确的磁盘或选择其他文件夹。
SelectDirectoryLabel=请指定下一张磁盘的位置。

; *** Installation status/log
StatusClosingApplications=正在关闭应用程序...
StatusCreateDirs=正在创建目录...
StatusExtractFiles=正在解压缩文件...
StatusCreateIcons=正在创建快捷方式...
StatusCreateIniEntries=正在创建 INI 条目...
StatusCreateRegistryEntries=正在创建注册表条目...
StatusRegisterFiles=正在注册文件...
StatusSavingUninstall=正在保存卸载信息...
StatusRunProgram=正在完成安装...
StatusRestartingApplications=正在重新启动应用程序...
StatusRollback=正在回滚更改...

; *** Misc
AdditionalIcons=附加快捷方式：
CreateDesktopIcon=创建桌面快捷方式(&D)
CreateQuickLaunchIcon=创建快速启动栏快捷方式(&Q)
ProgramOnTheWeb=%1 网站
UninstallProgram=卸载 %1
LaunchProgram=运行 %1
AssocFileExtension=将 %1 与 %2 文件扩展名关联(&A)
AssocingFileExtension=正在将 %1 与 %2 文件扩展名关联...
AutoStartProgramGroupDescription=启动组：
AutoStartProgram=自动启动 %1
AddonHostProgramNotFound=无法在您指定的文件夹中找到 %1。%n%n您是否仍要继续？

; *** Uninstall
UninstallStatusLabel=正在从计算机中卸载 %1，请稍候...
UninstalledAll=%1 已成功从计算机中卸载。
UninstalledMost=%1 卸载完成。%n%n某些元素无法被删除，您可以手动删除它们。
UninstalledAndNeedsRestart=要完成 %1 的卸载，必须重新启动计算机。%n%n您想现在重新启动吗？
UninstallDataCorrupted="%1"文件已损坏。无法卸载

; *** Shutdown block reasons
ShutdownBlockReasonInstallingApp=正在安装 %1。
ShutdownBlockReasonUninstallingApp=正在卸载 %1。
