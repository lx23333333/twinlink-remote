# 第三方组件说明

TwinLink 是独立的 C# 操作界面，使用进程调用与官方管理页面组合以下组件。当前分发包只含 TwinLink 自身、文档和安装向导，不捆绑任何第三方二进制、驱动或源码，也未修改这些项目。

| 组件 | 作用 | 上游与许可信息 |
| --- | --- | --- |
| Sunshine | 桌面采集、硬件编码、远程输入、PIN 配对 | https://github.com/LizardByte/Sunshine ，GPL-3.0；以对应发行版本 LICENSE 为准 |
| Moonlight Qt | 视频解码、显示、键鼠/音频串流 | https://github.com/moonlight-stream/moonlight-qt ，GPL-3.0；以对应发行版本 LICENSE 为准 |
| Tailscale | 两台电脑之间的加密跨网连接及 Taildrop 文件传输 | https://github.com/tailscale/tailscale ，开源代码 BSD-3-Clause；Windows 分发程序及托管服务另受官方条款约束 |
| WinGet | 从包源获取并校验安装组件 | https://github.com/microsoft/winget-cli ，MIT；包本身仍适用各自许可与条款 |

TwinLink 与上述项目不存在官方从属或背书关系。若未来捆绑或修改第三方程序，须重新处理许可证、通知与对应源码义务。
