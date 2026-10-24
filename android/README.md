# Android v1.4.1-android.1

Android 8.0（API 26）及以上的原生 Java 测试版，重新实现 Windows 时钟的主要功能。

- 24小时旋转表盘、固定当前时间线、北京/东京/伦敦/纽约/UTC时区。
- 沿用 markets.json 的107个市场/品种条目及2026内地节假日，支持国内91品种自选。
- 已结束时段变灰，当前及后续时段保留颜色；跨午夜按真实收盘时间判断。
- 东财指数及期货主连行情；腾讯指数、新浪期货备用。目标每秒请求，慢网不重叠，后台暂停。显示来源、源站时间、休市/滞后/失败状态。
- 显示模块开关与自选记忆；手动及每30分钟NTP校时，只调整应用显示时间。
- 移动端采用纵向滚动卡片；不包含 Windows 托盘、桌面置顶和系统悬浮窗。

## 安装

下载本仓库的 [APK](https://github.com/Domokira/GlobalFuturesClock/raw/refs/heads/main/downloads/GlobalFuturesClock-v1.4.1-android.1.apk)，在安卓设备允许该下载来源安装应用后打开。此包使用 Android 调试签名，仅供试用，不是应用商店正式发行版；后续更换签名可能需要卸载旧版（会清除偏好）。不申请存储、通讯录、位置或后台服务权限，仅访问网络。

## 构建与验证

JDK17、Gradle8.11.1、Android SDK35、Build Tools35；在 android 目录运行 `gradle :app:testDebugUnitTest :app:lintDebug :app:assembleDebug`。APK 输出到 `app/build/outputs/apk/debug/app-debug.apk`。

本次在 Windows 本地使用标准 Gradle 构建成功。8项单元测试通过：周日全部107条目休市、跨午夜夜盘收盘变灰、节假日与节前夜盘、时区裁日、夏令时跳过与重复小时、当前及未来时段颜色、无效报价拒绝、有效报价解析。Android Lint 无错误（保留中文文案国际化和备份规则提示），APK签名及ZIP对齐验证通过。

已提供模拟器启动测试源码；本次模拟器未能完成启动，尚未完成设备运行验证。

境外采用常规周历，节假日和临时调整仍需核对公告。免费行情端点不保证长期可用或逐笔实时；NTP不可达时使用系统时间。尚需不同实体手机的兼容性和盘中持续行情验证。
