# 安装

RC1 提供三种发布形式：

1. framework-dependent x64：需要 .NET 8 Desktop Runtime；
2. self-contained x64：不要求预装 .NET；
3. portable ZIP：解压后直接运行。

安装脚本是当前用户安装，不需要管理员权限：

    .\install.ps1 -PackagePath . -CreateShortcuts

开机自启默认关闭；只有显式添加 -StartWithWindows 才会建立当前用户 Run 项。首次运行不自动改变壁纸，用户确认后才进入匹配流程。安装不会修改显示器、音频、Explorer、服务、计划任务或电源，也不要求重启。

轻量模式为默认模式，TaskbarHost、ShellHost、ScreenSaverHost、RemoteHost 和 Online Wallpaper Providers 不创建进程。
