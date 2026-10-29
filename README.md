## AirPlay Windows App
一个支持多设备同时连接的 AirPlay 接收器，支持实时音频流、屏幕镜像、支持双向控制。  
使用 dotnet 10 编译、 WinUI 3 框架开发，具有良好的性能。

核心功能实现请参阅 [AirPlay.Core2](https://github.com/natsurainko/AirPlay.Core2) 项目  

### 功能

+ [x] 支持多个设备同时连接到 AirPlay 接收端
+ [x] 支持多设备 ALAC、AAC、AAC-ELD 格式的音频流投送
+ [x] 支持多设备 H.264 格式的屏幕镜像流投送
+ [x] 支持调整镜像流分辨率（1080p / 1440p / 2160p），以及最高帧率（30 / 60 / 120 FPS）[需要目标设备支持]
+ [x] 支持系统媒体传输控件（Smtc）来反向控制音频连接
+ [x] 支持全局快捷键 `Windows + Alt + A` 来快速打开设备控制面板

### 屏幕截图

<img width="1467" height="930" alt="e1dd854e4188aab975c65aced5c8251a" src="https://github.com/user-attachments/assets/89d5ba3b-6dc1-49e1-bfb5-885177946ff5" />

### 维护与更新

`AirPlay.App/Libraries` 中的 FFmpeg 原生 DLL 必须与 FFmpeg.AutoGen 绑定要求的版本及应用架构匹配；更新绑定时需同步检查原生库和项目打包条目。

