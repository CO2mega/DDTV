// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

using Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Desktop.Views.Pages;

/// <summary>
/// Interaction logic for DashboardPage.xaml
/// </summary>
public partial class HistoryPage
{
    public ObservableCollection<Core.RuntimeObject.Detect.History> histories = new();
    public HistoryPage()
    {
        InitializeComponent();
        HistoryView.ItemsSource = histories;
        foreach (var history in Core.RuntimeObject.Detect.histories)
        {
            histories.Add(history);
        }
        //RecEndEvent是Core层的静态事件，只订阅不退订会把页面实例永久钉在内存里（页面重建时还会叠加多重订阅）。
        //因此改为Loaded订阅、Unloaded退订，并在重新显示时同步隐藏期间错过的记录
        Loaded += HistoryPage_Loaded;
        Unloaded += HistoryPage_Unloaded;
    }

    private void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        //同步页面隐藏期间错过的录制历史（Core侧列表只增不减，尾部为最新记录，逐条倒序插入顶部保持新→旧）
        try
        {
            var coreHistories = Core.RuntimeObject.Detect.histories;
            int missing = coreHistories.Count - histories.Count;
            for (int i = 0; i < missing; i++)
            {
                var src = coreHistories[coreHistories.Count - 1 - i];
                histories.Insert(0, CopyHistory(src));
            }
        }
        catch (Exception)
        {
            //列表被录制线程并发修改时放弃本次同步，后续事件仍会补齐
        }
        //先减后加，保证幂等不重复订阅
        Core.RuntimeObject.Detect.RecEndEvent -= Detect_RecEndEvent;
        Core.RuntimeObject.Detect.RecEndEvent += Detect_RecEndEvent;
    }

    private void HistoryPage_Unloaded(object sender, RoutedEventArgs e)
    {
        Core.RuntimeObject.Detect.RecEndEvent -= Detect_RecEndEvent;
    }

    /// <summary>
    /// 拷贝一份历史记录，避免与 Core.RuntimeObject.Detect.histories 共享同一个可变引用
    /// </summary>
    private static Core.RuntimeObject.Detect.History CopyHistory(Core.RuntimeObject.Detect.History src) => new()
    {
        Name = src.Name,
        Time = src.Time,
        Title = src.Title
    };

    private void Detect_RecEndEvent(object? sender, EventArgs e)
    {
        // 发布方(DetectRoom.RecEndEvent)传入的 sender 即为已填充好的 History 对象
        if (sender is not Core.RuntimeObject.Detect.History src) return;
        //异步封送，不阻塞Core的录制结束线程
        Dispatcher.BeginInvoke(() =>
        {
            histories.Insert(0, CopyHistory(src));
        });
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", Path.GetFullPath(Config.Core_RunConfig._RecFileDirectory));
    }
}
