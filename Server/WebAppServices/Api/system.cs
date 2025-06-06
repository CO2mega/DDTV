﻿using Server.WebAppServices.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static Server.WebAppServices.Middleware.InterfaceAuthentication;
using static Core.Tools.SystemResource;
using static Core.Tools.SystemResource.GetHDDInfo;
using static Core.Tools.SystemResource.GetMemInfo;
using System.Net;
using static Core.Tools.SystemResource.Overview;

namespace Server.WebAppServices.Api
{
    [Produces(MediaTypeNames.Application.Json)]
    [ApiController]
    [Route("api/system/[controller]")]
    [Login]
    [Tags("config")]
    public class get_core_version : ControllerBase
    {
        /// <summary>
        /// 获取当前Core的版本号
        /// </summary>
        /// <returns></returns>
        [HttpGet(Name = "get_core_version")]
        public ActionResult Get(GetCommonParameters commonParameters)
        {
            return Content(MessageBase.MssagePack(nameof(get_core_version), System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(), "CoreVersion"), "application/json");
        }
    }
    [Produces(MediaTypeNames.Application.Json)]
    [ApiController]
    [Route("api/system/[controller]")]
    [Login]
    [Tags("config")]
    public class get_webui_version : ControllerBase
    {
        /// <summary>
        /// 获取当前WEBUI版本信息
        /// </summary>
        /// <returns></returns>
        [HttpGet(Name = "get_webui_version")]
        public ActionResult Get(GetCommonParameters commonParameters)
        {
            if(System.IO.File.Exists("./static/version.ini"))
            {
                string info = System.IO.File.ReadAllText("./static/version.ini");
                return Content(MessageBase.MssagePack(nameof(get_webui_version), info, "WebUIVersion"), "application/json");
            }
            else
            {
                return Content(MessageBase.MssagePack(nameof(get_webui_version), "", "WEBUI版本信息文件不存在"), "application/json");
            }
        }
    }
    [Produces(MediaTypeNames.Application.Json)]
    [ApiController]
    [Route("api/system/[controller]")]
    [Login]
    [Tags("config")]
    public class get_system_resources : ControllerBase
    {
        /// <summary>
        /// 获取内存和录制路径储存空间使用情况（请注意！该接口单次执行都在秒以上，且硬件开销较大，不推荐频繁调用用于刷新！如果一定要用，调用间隔推荐以分钟为单位）
        /// </summary>
        /// <returns></returns>
        [HttpGet(Name = "get_system_resources")]
        public ActionResult Get(GetCommonParameters commonParameters)
        {
            SystemResourceClass systemResourceClass = Core.Tools.SystemResource.Overview.GetOverview();
            return Content(MessageBase.MssagePack(nameof(get_system_resources), systemResourceClass, "SystemResource"), "application/json");
        }

    }

    [Produces(MediaTypeNames.Application.Json)]
    [ApiController]
    [Route("api/system/[controller]")]
    [Login]
    [Tags("config")]
    public class generate_debug_file_snapshot : ControllerBase
    {
        /// <summary>
        /// 生成debug快照文件
        /// </summary>
        /// <returns></returns>
        [HttpGet(Name = "get_debug_file_snapshot")]
        public ActionResult Get(GetCommonParameters commonParameters)
        {
            string file_path = Core.Tools.DebuggingRecord.GenerateReportSnapshot();
            return Content(MessageBase.MssagePack(nameof(generate_debug_file_snapshot), file_path, "GenerateReportSnapshot"), "application/json");
        }

    }

    [Produces(MediaTypeNames.Application.Json)]
    [ApiController]
    [Route("api/system/[controller]")]
    [Login]
    [Tags("config")]
    public class get_c : ControllerBase
    {
        /// <summary>
        /// 用于桌面端播放器配置
        /// </summary>
        /// <returns></returns>
        [HttpGet(Name = "get_c")]
        public ActionResult Get(GetCommonParameters commonParameters)
        {
            return Content(MessageBase.MssagePack(nameof(get_c), Core.RuntimeObject.Account.AccountInformation.strCookies, ""), "application/json");
        }
    }
}
