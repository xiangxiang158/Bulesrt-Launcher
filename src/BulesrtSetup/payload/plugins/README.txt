把插件文件夹放进来，每个插件一个子文件夹，里面至少要有 plugin.json 和它的 dll。

plugin.json 示例：
{ "id": "hello", "name": "Hello Plugin", "version": "1.0.0", "assembly": "HelloPlugin.dll" }

放好后重启启动器，或在 设置 → 数据 → 插件管理… 里查看加载结果。
