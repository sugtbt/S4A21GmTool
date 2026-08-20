# DfoGmToolA21

S4A21 服务端的 Web GM 控制台。由 A12 `DfoGmTool` 按当前 A21 服务端数据面迁移而来。

独立进程运行，直接操作 A21 服务端部署目录里的 `inventory.db` 和 `Script.pvf`；浏览器打开 `http://localhost:5051` 使用。

源码自包含：不依赖任何本地相邻仓库即可构建和发布。

## 与 A12 工具的差异

- 默认监听 **5051**，避免和 A12 GM（5050）抢端口
- 自动发现路径指向 `servers4a21\Server\DfoServer\bin\Debug`
- 物品核心为 A21 **99 字节 ItemCore**（82B 主体 + 17B A21 尾部）
- 数据库只走 A21 基线 `86jp-database-v1` / schema 迁移，不会对现有 A21 库跑 A12 的旧迁移链
- 任务完成标记读写 `character_quest_completions`
- 称号簿读写 `character_titlebook_items`
- 成就进度读写 `character_achievements`
- 背包主表读写 `character_inventory_items` / `account_inventory_items`
- 公会勋章容器 `list_type=38`（勋章 0-48 / 守护珠 49-97）和穿戴勋章槽 31
- 灵魂仓库槽 360-364 走 `accounts.soul_*`，与晶块一样在账号面板覆写
- 打开 A12 或其他非 A21 基线库会立即报错并停止解析

不要用本工具打开 A12 的 `inventory.db`。

## 功能

与 A12 GM 工具相同：账号货币/晶块/灵魂仓库/金库、角色等级/转职/SP-TP、背包查看与删除（含公会勋章容器）、角色邮箱查看/删除/清空、邮件发放物品、任务完成链、称号簿成就。

**发放物品**默认经游戏内邮件：`MailboxRepository.SendSystemMail` 写入 99B ItemCore 附件。在线角色进邮箱即可领取。

直改数据库的操作（删除/货币覆写/直写背包）对在线角色需要返回选角再进入才会生效。

## 构建与运行

```
dotnet build DfoGmTool.csproj -c Debug
dotnet run --project DfoGmTool.csproj
```

服务端数据目录按以下顺序定位（找到含 `Data/inventory.db` + `Data/Pvf/Script.pvf` 的目录为止）：

1. 命令行参数 `--server-bin <路径>`
2. 环境变量 `DFO_GM_SERVER_BIN`
3. 从工作目录/程序目录逐级向上，找同级的 `servers4a21\Server\DfoServer\bin\Debug`

本机典型路径：

```
G:\gitwork\servers4a21\Server\DfoServer\bin\Debug
```

`item_schema.sql` 优先用服务端目录里的，缺失时回退工具自带的 A21 schema 拷贝。

## 发布

```
dotnet publish DfoGmTool.csproj -c Release -r win-x64 --self-contained true -o bin\publish
```

目标机器上用 `--server-bin` 或环境变量指向该机的 **A21** 服务端数据目录。

## 注意

- 改动数据库前建议备份 `inventory.db`
- 强制完成任务不发任务奖励；想拿奖励用「标记可交」然后回城正常交付
- 打开 A12 或其他非 A21 基线库会立即报错并停止解析，不会读 82B ItemCore
- 公会勋章容器（`list_type=38`，勋章 0-48 / 守护珠 49-97）和穿戴勋章槽 31 可查看删除
- 灵魂仓库槽 360-364 与晶块一样在账号面板覆写
- 角色邮箱可查看收件箱/保管邮件（含已过期），支持单封删除和一键清空；删除只打收件人删除标记，未领取附件不会进背包
- 物品/任务索引在启动后后台构建，页面顶部显示状态；构建完成前发放不校验物品 ID
