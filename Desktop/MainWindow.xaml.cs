using Core;
using Core.Account;
using Core.LogModule;
using Core.RuntimeObject;
using Desktop.Models;
using Desktop.Views.Pages;
using Desktop.Views.Windows;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Notification.Wpf;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;
using static Core.RuntimeObject.Detect;
using static Core.Tools.DokiDoki;

namespace Desktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        /// <summary>
        /// 系统托盘通知
        /// </summary>
        public static NotificationManager notificationManager = new NotificationManager();
        /// <summary>
        /// 确认窗口
        /// </summary>
        public static IContentDialogService _contentDialogService = new ContentDialogService();
        /// <summary>
        /// 程序关闭标志
        /// </summary>
        public static bool IsProgrammaticClose = false;
        /// <summary>
        /// 底部提示框
        /// </summary>
        public static ISnackbarService SnackbarService;
        /// <summary>
        /// 是否连接远程服务器
        /// </summary>
        public static bool ToConnectToRemoteServer = false;
        /// <summary>
        /// 更新目录房间列表录制中数量定时器
        /// </summary>
        private System.Threading.Timer IpvDetectionTimer;

        public static Config.RunConfig configViewModel { get; set; } = new();

        public static string P_Title = string.Empty;

        public MainWindow()
        {
            if (Application_Startup())
            {
                Environment.Exit(Core.Init.ExitCodes.FatalError);
                return;
            }
            InitializeComponent();

            this.DataContext = configViewModel;
            try
            {
                //初始化各种page
                Init();
                InitializeNotifyIcon();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"UI初始化出现重大错误，错误堆栈{ex.ToString()}");
            }
            Version dotnetVersion = Environment.Version;
        }

        /// <summary>
        /// 初始化各种页面内容
        /// </summary>
        public void Init()
        {
            //设置房间卡片列表页定时任务（首次10ms立即触发，之后每1秒）
            DataPage.Timer_DataPage = new System.Threading.Timer(DataPage.Refresher, null, 10, 1000);
            //设置登录失效事件（失效后弹出扫码框）
            DataSource.LoginStatus.LoginFailureEvent += LoginStatus_LoginFailureEvent;
            //设置登录态检测定时任务
            DataSource.LoginStatus.Timer_LoginStatus = new System.Threading.Timer(DataSource.LoginStatus.RefreshLoginStatus, null, 1000 * 10, 1000 * 60 * 30);
            //版本更新检测
            Core.Tools.ProgramUpdates.NewVersionAvailableEvent += ProgramUpdates_NewVersionAvailableEvent;
            //设置默认显示页
            Loaded += (_, _) => RootNavigation.Navigate(typeof(DefaultPage));
            //初始化底部提示框
            SnackbarService = Desktop.App._MainSnackbarServiceProvider.GetRequiredService<ISnackbarService>();
            SnackbarService.SetSnackbarPresenter(MainSnackbar);
            //初始化托盘
            InitializeNotifyIcon();
            //初始化确认窗口
            _contentDialogService.SetDialogHost(RootContentDialogPresenter);
            //初始化标题和远程模式标志以及检查远程和本地版本号一致性
            _ = InitializeTitleModeAsync();
            //监听开播事件，用于开播提醒
            Detect.detectRoom.LiveStart += DetectRoom_LiveStart;
            //初始化VLC播放器组件
            LibVLCSharp.Shared.Core.Initialize("./plugins/vlc");
            //初始化系统休眠设置
            if (Config.Core_RunConfig._PreventWindowsHibernation)
            {
                WindowsAPI.CloseWindowsHibernation();
            }
            else
            {
                WindowsAPI.OpenWindowsHibernation();
            }
            //更新目录房间列表录制中数量（改为异步）
            IpvDetectionTimer = new System.Threading.Timer(async _ => await UpdateNumberRecordedRoomsInDirectoryRoomListAsync(), null, 1000, 1000);
        }

        /// <summary>
        /// 异步初始化标题和远程模式
        /// </summary>
        private async Task InitializeTitleModeAsync()
        {
            if (!Config.Core_RunConfig._DesktopIP.Contains("//127.") && !Config.Core_RunConfig._DesktopIP.Contains("//0.") && !Config.Core_RunConfig._DesktopIP.Contains("localhost"))
            {
                ToConnectToRemoteServer = true;
            }

            int retryCount = 0;
            const int maxRetries = 10;

            while (retryCount < maxRetries)
            {
                try
                {
                    DokiClass doki;
                    if (Core.Config.Core_RunConfig._DesktopRemoteServer || Core.Config.Core_RunConfig._LocalHTTPMode)
                    {
                        doki = await NetWork.Get.GetBodyAsync<DokiClass>($"{Config.Core_RunConfig._DesktopIP}:{Config.Core_RunConfig._DesktopPort}/api/dokidoki");
                    }
                    else
                    {
                        doki = Core.Tools.DokiDoki.GetDoki();
                    }

                    if (doki != null)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (Core.Init.Ver != doki.Ver)
                            {
                                MainWindow.SnackbarService.Show("远程版本不一致", $"检测到远程模式下远程版本与本地Desktop版本不一致！\n本地Desktop版本号:【{Core.Init.Ver}】|远程版本号:【{doki.Ver}】", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(5));
                                this.Title = $"{doki.InitType}|本地 {Core.Init.Ver}|远程 {doki.Ver}| %%% |{Enum.GetName(typeof(Config.Mode), doki.StartMode)}【{doki.CompilationMode}】(编译时间:{doki.CompiledVersion}){(ToConnectToRemoteServer ? "【远程模式】" : "")}$$$";
                            }
                            else
                            {
                                this.Title = $"{doki.InitType}|{doki.Ver}| %%% |{Enum.GetName(typeof(Config.Mode), doki.StartMode)}【{doki.CompilationMode}】(编译时间:{doki.CompiledVersion}){(ToConnectToRemoteServer ? "【远程模式】" : "")}$$$";
                            }
                            P_Title = this.Title;
                        });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(InitializeTitleModeAsync), "初始化标题失败", ex, false);
                }

                retryCount++;
                if (retryCount < maxRetries)
                {
                    await Task.Delay(8000);
                }
            }
        }

        public bool Application_Startup()
        {
            Process process = RunningInstance();
            if (process != null)
            {
                System.Windows.MessageBoxResult result = System.Windows.MessageBox.Show(
                    "已经有DDTV的Desktop实例正在运行中" +
                   "\r点击'是'强制启动一个新DDTV" +
                   "\r点击'否'阻止打开新窗口和新DDTV" +
                   $"\r======参考信息======" +
                   $"\rId:{process.Id}" +
                   $"\rProcessName:{process.ProcessName}"
                   , "已有DDTV实例正在运行", System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    System.Threading.Thread.Sleep(500);
                    return true;
                }
            }
            return false;
        }

        public static Process RunningInstance(bool IsStart = true)
        {
            try
            {
                Process currentProcess = Process.GetCurrentProcess();
                Process[] Processes = Process.GetProcessesByName(currentProcess.ProcessName);
                foreach (Process process in Processes)
                {
                    if (!IsStart || process.Id != currentProcess.Id)
                    {
                        string PA = Assembly.GetExecutingAssembly().Location.Replace("/", "\\");
                        string PB = currentProcess.MainModule.FileName;
                        string PAA = PA.Replace(PA.Split('.')[PA.Split('.').Length - 1], "");
                        string PBA = PB.Replace(PB.Split('.')[PB.Split('.').Length - 1], "");
                        if (PAA == PBA)
                        {
                            return process;
                        }
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// 初始化托盘图标
        /// </summary>
        private void InitializeNotifyIcon()
        {
            NotifyIcon notifyIconWindow = new NotifyIcon();
            StateChanged += MainWindow_StateChanged;
        }

        /// <summary>
        /// 窗口缩小事件
        /// </summary>
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (Config.Core_RunConfig._ZoomOutMode != 0 && this.WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
        }

        /// <summary>
        /// 开播事件，触发开播提醒
        /// </summary>
        private void DetectRoom_LiveStart(object? sender, (RoomCardClass Card, bool Danma_MessageReceived) LiveInvoke)
        {
            RoomCardClass roomCard = LiveInvoke.Card;
            List<TriggerType> triggerTypes = sender as List<TriggerType> ?? new List<TriggerType>();
            if (roomCard.IsRemind && triggerTypes.Contains(TriggerType.RegularTasks))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    notificationManager.Show(new NotificationContent
                    {
                        Title = "DDTV-开播提醒",
                        Message = $"【{roomCard.Name}】的直播开始啦",
                        Type = NotificationType.Information
                    });
                });
            }
        }

        /// <summary>
        /// 新版本检测事件
        /// </summary>
        private void ProgramUpdates_NewVersionAvailableEvent(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                MainWindow.SnackbarService.Show("检测到更新", $"检测到DDTV新版本：【{sender}】，{(ToConnectToRemoteServer ? "请更新远程服务端后，再到设置页面点击更新按钮进行更新" : "请到设置页面点击更新按钮进行更新")}", ControlAppearance.Primary, new SymbolIcon(SymbolRegular.DocumentHeaderArrowDown20), TimeSpan.FromSeconds(5));
            });
        }

        /// <summary>
        /// 登陆失效事件
        /// </summary>
        private void LoginStatus_LoginFailureEvent(object? sender, EventArgs e)
        {
            if (!DataSource.LoginStatus.LoginWindowDisplayStatus)
            {
                DataSource.LoginStatus.LoginWindowDisplayStatus = true;
                Dispatcher.InvokeAsync(() =>
                {
                    if (Core.Init.GetRunTime() < 90)
                    {
                        QrLogin qrLogin = new QrLogin();
                        qrLogin.ShowDialog();
                    }
                    else
                    {
                        MainWindow.SnackbarService.Show("登录态检查失败", $"检查账号信息的登陆状态有效性失败，该提示一般是由于登录态已过期造成的，请尝试重新登陆", ControlAppearance.Primary, new SymbolIcon(SymbolRegular.CloudError20), TimeSpan.FromSeconds(30));
                    }
                });
            }
        }

        /// <summary>
        /// 关闭后事件
        /// </summary>
        private void Window_Closed(object sender, EventArgs e)
        {
            if (!IsProgrammaticClose)
            {
                DataPage.Timer_DataPage?.Dispose();
                DataSource.LoginStatus.Timer_LoginStatus?.Dispose();
                Environment.Exit(Core.Init.ExitCodes.FatalError);
            }
        }

        /// <summary>
        /// 关闭前确认关闭
        /// </summary>
        private async void FluentWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!IsProgrammaticClose)
            {
                e.Cancel = true;

                bool shouldExit = await ShowExitConfirmationAsync();
                if (shouldExit)
                {
                    Environment.Exit(Core.Init.ExitCodes.FatalError);
                }
            }
        }

        /// <summary>
        /// 执行退出确认逻辑（供外部调用）
        /// </summary>
        public async Task<bool> ShowExitConfirmationAsync()
        {
            var messageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "关闭确认",
                Content = "确认要关闭DDTV吗？\r\n关闭后所有录制任务以及播放窗口均会结束。",
                PrimaryButtonText = "是",
                SecondaryButtonText = "否",
                IsCloseButtonEnabled = false,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var result = await messageBox.ShowDialogAsync();

            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                DataPage.Timer_DataPage?.Dispose();
                DataSource.LoginStatus.Timer_LoginStatus?.Dispose();
                IsProgrammaticClose = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 异步更新目录房间列表录制中数量
        /// </summary>
        public static async Task UpdateNumberRecordedRoomsInDirectoryRoomListAsync()
        {
            try
            {
                (int MonitoringCount, int LiveCount, int RecCount) count = new();

                if (Core.Config.Core_RunConfig._DesktopRemoteServer || Core.Config.Core_RunConfig._LocalHTTPMode)
                {
                    count = await NetWork.Post.PostBody<(int MonitoringCount, int LiveCount, int RecCount)>(
                        $"{Config.Core_RunConfig._DesktopIP}:{Config.Core_RunConfig._DesktopPort}/api/get_rooms/room_statistics");
                }
                else
                {
                    count = Core.RuntimeObject._Room.Overview.GetRoomStatisticsOverview();
                }

                configViewModel.DataPageTitle = $"房间列表 ({count.RecCount})";
                configViewModel.OnPropertyChanged("DataPageTitle");

                configViewModel.ProgramTitle = P_Title.Replace("%%%", $"{count.RecCount}录制中|{count.LiveCount}开播中|{count.MonitoringCount}监控中")
                    .Replace("$$$", $"{(!Core.RuntimeObject.Account.AccountInformation.State ? "【警告！登陆态已失效】" : "")}");
                configViewModel.OnPropertyChanged("ProgramTitle");
            }
            catch (Exception ex)
            {
                Log.Warn(nameof(UpdateNumberRecordedRoomsInDirectoryRoomListAsync), "更新房间统计出现错误", ex, false);
            }
        }
    }
}
