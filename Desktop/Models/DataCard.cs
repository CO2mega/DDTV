using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Desktop.Models
{
    /// <summary>
    /// 房间卡片数据模型，支持属性变更通知以实现单字段增量更新
    /// </summary>
    public class DataCard : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private long _uid;
        public long Uid
        {
            get => _uid;
            set { if (_uid != value) { _uid = value; OnPropertyChanged(nameof(Uid)); } }
        }

        private long _roomId;
        public long Room_Id
        {
            get => _roomId;
            set { if (_roomId != value) { _roomId = value; OnPropertyChanged(nameof(Room_Id)); } }
        }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(nameof(Title)); } }
        }

        private string _nickname = string.Empty;
        public string Nickname
        {
            get => _nickname;
            set { if (_nickname != value) { _nickname = value; OnPropertyChanged(nameof(Nickname)); } }
        }

        private bool _isRec;
        public bool IsRec
        {
            get => _isRec;
            set
            {
                if (_isRec != value)
                {
                    _isRec = value;
                    OnPropertyChanged(nameof(IsRec));
                    OnPropertyChanged(nameof(RecSign));
                }
            }
        }

        public SolidColorBrush RecSign => GetRecSignBrush(_isRec, _isDownload);

        private bool _isDanmu;
        public bool IsDanmu
        {
            get => _isDanmu;
            set
            {
                if (_isDanmu != value)
                {
                    _isDanmu = value;
                    OnPropertyChanged(nameof(IsDanmu));
                    OnPropertyChanged(nameof(DanmuSign));
                }
            }
        }

        public SolidColorBrush DanmuSign => GetDanmuSignBrush(_isDanmu, _isDanmaRecording);

        private bool _isRemind;
        public bool IsRemind
        {
            get => _isRemind;
            set
            {
                if (_isRemind != value)
                {
                    _isRemind = value;
                    OnPropertyChanged(nameof(IsRemind));
                    OnPropertyChanged(nameof(RemindSign));
                }
            }
        }

        public SolidColorBrush RemindSign => GetRemindSignBrush(_isRemind);

        private bool _recStatus;
        public bool Rec_Status
        {
            get => _recStatus;
            set
            {
                if (_recStatus != value)
                {
                    _recStatus = value;
                    OnPropertyChanged(nameof(Rec_Status));
                    OnPropertyChanged(nameof(Rec_Status_IsVisible));
                    OnPropertyChanged(nameof(Live_Status));
                    OnPropertyChanged(nameof(Live_Status_IsVisible));
                    OnPropertyChanged(nameof(Rest_Status));
                    OnPropertyChanged(nameof(Rest_Status_IsVisible));
                }
            }
        }

        public Visibility Rec_Status_IsVisible => _recStatus ? Visibility.Visible : Visibility.Collapsed;

        private bool _liveStatus;
        public bool Live_Status
        {
            get => !_recStatus && _liveStatus;
            set
            {
                if (_liveStatus != value)
                {
                    _liveStatus = value;
                    OnPropertyChanged(nameof(Live_Status));
                    OnPropertyChanged(nameof(Live_Status_IsVisible));
                    OnPropertyChanged(nameof(Rest_Status));
                    OnPropertyChanged(nameof(Rest_Status_IsVisible));
                }
            }
        }

        public Visibility Live_Status_IsVisible => !_recStatus && _liveStatus ? Visibility.Visible : Visibility.Collapsed;

        public bool Rest_Status => !_liveStatus;
        public Visibility Rest_Status_IsVisible => (!_liveStatus && !_recStatus) ? Visibility.Visible : Visibility.Collapsed;

        private double _downloadSpe;
        public double DownloadSpe
        {
            get => _downloadSpe;
            set
            {
                if (_downloadSpe != value)
                {
                    _downloadSpe = value;
                    OnPropertyChanged(nameof(DownloadSpe));
                }
            }
        }

        private string _downloadSpeStr = string.Empty;
        public string DownloadSpe_str
        {
            get => _downloadSpeStr;
            set { if (_downloadSpeStr != value) { _downloadSpeStr = value; OnPropertyChanged(nameof(DownloadSpe_str)); } }
        }

        private bool _isDownload;
        public bool IsDownload
        {
            get => _isDownload;
            set
            {
                if (_isDownload != value)
                {
                    _isDownload = value;
                    OnPropertyChanged(nameof(IsDownload));
                    OnPropertyChanged(nameof(RecSign));
                    OnPropertyChanged(nameof(Rec_Status));
                    OnPropertyChanged(nameof(Rec_Status_IsVisible));
                    OnPropertyChanged(nameof(Live_Status));
                    OnPropertyChanged(nameof(Live_Status_IsVisible));
                    OnPropertyChanged(nameof(Rest_Status));
                    OnPropertyChanged(nameof(Rest_Status_IsVisible));
                }
            }
        }

        private long _liveTime;
        public long LiveTime
        {
            get => _liveTime;
            set { if (_liveTime != value) { _liveTime = value; OnPropertyChanged(nameof(LiveTime)); } }
        }

        private string _liveTimeStr = string.Empty;
        public string LiveTime_str
        {
            get => _liveTimeStr;
            set { if (_liveTimeStr != value) { _liveTimeStr = value; OnPropertyChanged(nameof(LiveTime_str)); } }
        }

        /// <summary>
        /// 内部字段：弹幕是否正在录制中（用于 DanmuSign 颜色判断）
        /// </summary>
        private bool _isDanmaRecording;
        public bool IsDanmaRecording
        {
            get => _isDanmaRecording;
            set
            {
                if (_isDanmaRecording != value)
                {
                    _isDanmaRecording = value;
                    OnPropertyChanged(nameof(DanmuSign));
                }
            }
        }

        #region Static Brush Cache

        private static readonly SolidColorBrush _grayBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777"));
        private static readonly SolidColorBrush _blueBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00aeec"));
        private static readonly SolidColorBrush _pinkBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#fb7299"));

        private static SolidColorBrush GetRecSignBrush(bool isRec, bool isDownload)
        {
            if (!isRec) return _grayBrush;
            return isDownload ? _pinkBrush : _blueBrush;
        }

        private static SolidColorBrush GetDanmuSignBrush(bool isDanmu, bool isRecording)
        {
            if (!isDanmu) return _grayBrush;
            return isRecording ? _pinkBrush : _blueBrush;
        }

        private static SolidColorBrush GetRemindSignBrush(bool isRemind)
        {
            return isRemind ? _blueBrush : _grayBrush;
        }

        #endregion
    }
}
