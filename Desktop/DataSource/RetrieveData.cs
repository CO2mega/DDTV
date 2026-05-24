using Core;
using Core.LogModule;
using Core.RuntimeObject;
using Desktop.Models;
using Desktop.Views.Pages;
using System.Windows;

namespace Desktop.DataSource
{
    /// <summary>
    /// Desktop 层的数据拉取入口，主要负责把 Core/远程 API 的数据同步到 UI 上
    /// </summary>
    internal class RetrieveData
    {
        /// <summary>
        /// 房间卡片相关的数据操作，包括拉取、排序、更新
        /// </summary>
        public class UI_RoomCards
        {
            /// <summary>
            /// 刷新房间卡片列表。先从 Core（或远程）拿数据，再和本地 UI 集合做增量同步。
            /// 避免直接 Clear 整个列表导致闪烁，采用 diff 更新的方式。
            /// </summary>
            public static void RefreshRoomCards()
            {
                if (DataPage.CardsCollection == null)
                {
                    return;
                }

                Core.RuntimeObject._Room.Overview.CardData Cards = new();

                // 判断走远程接口还是直接调本地 Core
                if (Core.Config.Core_RunConfig._DesktopRemoteServer || Core.Config.Core_RunConfig._LocalHTTPMode)
                {
                    Dictionary<string, string> dir = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(DataPage.screen_name))
                    {
                        dir = new Dictionary<string, string>()
                        {
                            {"screen_name",DataPage.screen_name }
                        };
                    }
                    else
                    {
                        dir = new Dictionary<string, string>()
                        {
                            {"quantity","102" },
                            {"page",DataPage.PageIndex.ToString() },
                            {"type",DataPage.CardType.ToString() },
                            {"screen_name","" }
                        };
                    }
                    Cards = NetWork.Post.PostBody<Core.RuntimeObject._Room.Overview.CardData>($"{Config.Core_RunConfig._DesktopIP}:{Config.Core_RunConfig._DesktopPort}/api/get_rooms/batch_complete_room_information", dir).Result;
                }
                else
                {
                    if (!string.IsNullOrEmpty(DataPage.screen_name))
                    {
                        Cards = Core.RuntimeObject._Room.Overview.GetCardOverview(0, 0, Core.RuntimeObject._Room.SearchType.All, DataPage.screen_name);
                    }
                    else
                    {
                        Cards = Core.RuntimeObject._Room.Overview.GetCardOverview(102, DataPage.PageIndex, (_Room.SearchType)DataPage.CardType);
                    }
                }

                if (Cards == null)
                {
                    Log.Warn(nameof(RefreshRoomCards), "调用Core的API[batch_complete_room_information]获取房间信息失败，获取到的信息为Null", null, true);
                    return;
                };

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // 根据总数算一下需要多少页
                    int pg = (Cards.total / 102) + (Cards.total % 102 > 0 ? 1 : 0);
                    if (DataPage.PageCount != pg)
                    {
                        DataPage.PageCount = pg;
                        DataPage.UpdatePageCount(DataPage.PageCount);
                    }

                    // 先 diff 出本地有、但服务端已经删掉的房间，从 UI 里移除
                    List<long> _uid_Web = new List<long>();
                    foreach (var item in Cards.completeInfoList)
                    {
                        _uid_Web.Add(item.uid);
                    }
                    List<long> _uid_local = new List<long>();
                    foreach (var item in Views.Pages.DataPage.CardsCollection)
                    {
                        _uid_local.Add(item.Uid);
                    }
                    List<long> result = _uid_local.Except(_uid_Web).ToList();
                    foreach (var item in result)
                    {
                        Views.Pages.DataPage.CardsCollection.Remove(Views.Pages.DataPage.CardsCollection.FirstOrDefault(i => i.Uid == item));
                    }

                    // 遍历服务端返回的列表，逐个更新或插入
                    // 顺序已经在 Core 层排好了，这里只需要保证插入位置正确即可
                    foreach (var item in Cards.completeInfoList)
                    {
                        var card = Views.Pages.DataPage.CardsCollection.FirstOrDefault(i => i.Uid == item.uid);
                        if (card != null && card.Uid != 0)
                        {
                            // 有变化才更新，减少无意义的属性通知
                            if (
                                card.Title != item.roomInfo.title
                                || card.Live_Status != item.roomInfo.liveStatus
                                || card.Nickname != item.userInfo.name
                                || card.Room_Id != item.roomId
                                || card.IsRec != item.userInfo.isAutoRec
                                || card.IsDanmu != item.userInfo.isRecDanmu
                                || card.IsRemind != item.userInfo.isRemind
                                || card.IsDownload != item.taskStatus.isDownload
                                || card.DownloadSpe != item.taskStatus.downloadRate
                                || card.LiveTime != (new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds() - item.roomInfo.liveTime)
                                )
                            {
                                DataCard dataCard = CreateDataCard(item);

                                // 判断排序相关的 key 有没有变（录制/直播/自动录制/提醒）
                                // 如果变了，不能只改属性，必须挪位置才能保证顺序正确
                                bool sortKeyChanged = card.IsDownload != dataCard.IsDownload
                                                   || card.Live_Status != dataCard.Live_Status
                                                   || card.IsRec != dataCard.IsRec
                                                   || card.IsRemind != dataCard.IsRemind;

                                if (sortKeyChanged)
                                {
                                    Views.Pages.DataPage.CardsCollection.Remove(card);
                                    InsertCardWithCorrectOrder(dataCard);
                                }
                                else
                                {
                                    int index = Views.Pages.DataPage.CardsCollection.IndexOf(card);
                                    Views.Pages.DataPage.CardsCollection[index] = dataCard;
                                }
                            }
                        }
                        else
                        {
                            // 本地没有，是新房间，按规则插到对应位置
                            DataCard dataCard = CreateDataCard(item);
                            InsertCardWithCorrectOrder(dataCard);
                        }
                    }
                });
            }

            /// <summary>
            /// 按优先级把卡片插到正确位置。
            /// 顺序和 Core 层的 GetCardListClone 保持一致：
            /// 录制中 > 正在直播 > 开了自动录制 > 开了提醒 > 其他
            /// </summary>
            private static void InsertCardWithCorrectOrder(DataCard dataCard)
            {
                // 1. 录制中：插到第一个非录制中的前面
                if (dataCard.IsDownload)
                {
                    for (int i = 0; i < DataPage.CardsCollection.Count; i++)
                    {
                        if (!DataPage.CardsCollection[i].IsDownload)
                        {
                            Views.Pages.DataPage.CardsCollection.Insert(i, dataCard);
                            return;
                        }
                    }
                }
                // 2. 正在直播（但没在录制）：插到第一个不在直播的卡片前面
                else if (dataCard.Live_Status)
                {
                    for (int i = 0; i < DataPage.CardsCollection.Count; i++)
                    {
                        if (!DataPage.CardsCollection[i].IsDownload && !DataPage.CardsCollection[i].Live_Status)
                        {
                            Views.Pages.DataPage.CardsCollection.Insert(i, dataCard);
                            return;
                        }
                    }
                }
                // 3. 没开播但开了自动录制
                else if (dataCard.IsRec)
                {
                    for (int i = 0; i < DataPage.CardsCollection.Count; i++)
                    {
                        if (!DataPage.CardsCollection[i].IsDownload && !DataPage.CardsCollection[i].Live_Status && !DataPage.CardsCollection[i].IsRec)
                        {
                            Views.Pages.DataPage.CardsCollection.Insert(i, dataCard);
                            return;
                        }
                    }
                }
                // 4. 只开了提醒
                else if (dataCard.IsRemind)
                {
                    for (int i = 0; i < DataPage.CardsCollection.Count; i++)
                    {
                        if (!DataPage.CardsCollection[i].IsDownload && !DataPage.CardsCollection[i].Live_Status && !DataPage.CardsCollection[i].IsRec && !DataPage.CardsCollection[i].IsRemind)
                        {
                            Views.Pages.DataPage.CardsCollection.Insert(i, dataCard);
                            return;
                        }
                    }
                }

                // 上面都没匹配到（比如集合为空，或者所有卡片优先级都比当前高），直接放末尾
                if (!Views.Pages.DataPage.CardsCollection.Contains(dataCard))
                {
                    Views.Pages.DataPage.CardsCollection.Add(dataCard);
                }
            }

            /// <summary>
            /// 把 Core 返回的原始数据转成 UI 用的 DataCard
            /// </summary>
            private static DataCard CreateDataCard(Core.RuntimeObject._Room.Overview.CardData.CompleteInfo item)
            {
                long liveSeconds = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds() - item.roomInfo.liveTime;
                DataCard dataCard = new DataCard
                {
                    Uid = item.uid,
                    Room_Id = item.roomId,
                    Title = item.roomInfo.title,
                    Nickname = item.userInfo.name,
                    IsRec = item.userInfo.isAutoRec,
                    IsDanmu = item.userInfo.isRecDanmu,
                    IsRemind = item.userInfo.isRemind,
                    IsDownload = item.taskStatus.isDownload,
                    IsDanmaRecording = item.taskStatus.isDanma,
                    Rec_Status = item.taskStatus.isDownload,
                    Live_Status = item.roomInfo.liveStatus,
                    DownloadSpe = item.taskStatus.downloadRate,
                    DownloadSpe_str = item.taskStatus.isDownload ? Core.Tools.Linq.ConversionSize(item.taskStatus.downloadRate, Core.Tools.Linq.ConversionSizeType.BitRate) : "",
                    LiveTime = liveSeconds,
                    LiveTime_str = item.roomInfo.liveStatus ? ("已直播 " + TimeSpan.FromSeconds(liveSeconds).ToString(@"hh\:mm\:ss")) : ""
                };
                return dataCard;
            }
        }

        /// <summary>
        /// 房间配置修改，根据当前模式决定走远程 HTTP 还是本地 Core 调用
        /// </summary>
        public class RoomInfo
        {
            public static void ModifyRoomSettings(long uid, bool IsAutoRec, bool IsRecDanmu, bool IsRemind)
            {
                if (Core.Config.Core_RunConfig._DesktopRemoteServer || Core.Config.Core_RunConfig._LocalHTTPMode)
                {
                    Dictionary<string, string> dic = new Dictionary<string, string>
                    {
                        {"uid", uid.ToString() },
                        {"AutoRec",IsAutoRec.ToString() },
                        {"Remind",IsRemind.ToString() },
                        {"RecDanmu",IsRecDanmu.ToString() },
                    };
                    Task.Run(() =>
                    {
                        if (NetWork.Post.PostBody<bool>($"{Config.Core_RunConfig._DesktopIP}:{Config.Core_RunConfig._DesktopPort}/api/set_rooms/modify_room_settings", dic).Result)
                        {
                            Log.Info(nameof(ModifyRoomSettings), "调用Core的API[modify_room_settings]修改房间配置成功");
                        }
                        else
                        {
                            Log.Warn(nameof(ModifyRoomSettings), "调用Core的API[modify_room_settings]修改房间配置失败");
                        }
                    });
                }
                else
                {
                    Task.Run(() =>
                    {
                        if (Core.RuntimeObject._Room.ModifyRoomSettings(uid, IsAutoRec, IsRemind, IsRecDanmu))
                        {
                            Log.Info(nameof(ModifyRoomSettings), "调用Core的API[modify_room_settings]修改房间配置成功");
                        }
                        else
                        {
                            Log.Warn(nameof(ModifyRoomSettings), "调用Core的API[modify_room_settings]修改房间配置失败");
                        }
                    });
                }
            }
        }
    }
}
